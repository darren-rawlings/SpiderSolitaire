using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Renegade.Cards;
using Renegade.Ui;
using SpiderSolitaire.Core;

namespace SpiderSolitaire.Game;

/// <summary>
/// Spider solitaire with two packs. Ten piles across the table, the stock top right (one stack
/// for each deal left) and the suits taken off top left. Tap a card to move it and the cards on
/// it to the best place; tap the stock to deal a card onto every pile. A full suit from king to
/// ace comes off by itself. Undo is free and Hint shows a good move.
/// </summary>
public partial class Main : CardGame
{
	private const float Column = 0.68f, TopZ = -3.5f, TableauZ = -2.0f, Bottom = 3.7f, DownStep = 0.12f, UpStep = 0.3f;

	private readonly Card3D[] _nodes = new Card3D[104];
	private readonly Stack<int> _actions = new();
	private Spider? _game;
	private Button _undo = null!;
	private Button _hint = null!;
	private Node3D? _slots;
	private bool _counted;

	protected override string Title => "Spider Solitaire";
	protected override string Hint => "Tap a card to move it";
	protected override bool HasLevels => false;
	protected override bool HoldsCards => false;
	protected override bool Solitaire => true;
	protected override float LayoutWidth => 6.9f;
	protected override string NewGameText => "New deal";
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => _game is { IsWon: false, Moves: > 0 };

	private int Suits => Settings.Extra.TryGetValue("suits", out var s) && int.TryParse(s, out int n) && n is 1 or 2 or 4 ? n : 1;

	protected override void AddSettings()
	{
		var choices = new[] { 1, 2, 4 };
		Hud.AddChoice("Suits", new[] { "One suit", "Two suits", "Four suits" }, System.Array.IndexOf(choices, Suits), choice =>
		{
			Settings.Extra["suits"] = choices[choice].ToString();
			Settings.Save();
			if (!InProgress)
				Run(StartRound());
		});

		_undo = Hud.AddButton("Undo", false, Control.LayoutPreset.BottomLeft, 20, 180, -186, -130, 22);
		_undo.Pressed += () =>
		{
			if (!Busy && _game is { CanUndo: true } game)
			{
				int back = _actions.Count > 0 ? _actions.Pop() : game.History - 1;
				while (game.History > back)
					game.Undo();
				Hud.SetNewGameVisible(false);
				Run(Show("Undone."));
			}
		};
		_hint = Hud.AddButton("Hint", false, Control.LayoutPreset.BottomRight, -180, -20, -186, -130, 22);
		_hint.Pressed += () =>
		{
			if (!Busy && _game is { IsWon: false })
				Run(ShowHint());
		};
		_undo.Visible = _hint.Visible = false;
	}

	// ------------------------------------------------------------------ Where everything goes

	private static float X(int column) => (column - 4.5f) * Column;
	private static Vector3 StockAt => new(X(9), CardTable.FeltY, TopZ);
	private static Vector3 DoneAt => new(X(0), CardTable.FeltY, TopZ);
	private static Vector3 Lifted(Vector3 at, int height) => at + Vector3.Up * (Dealer.CardThickness * (height + 1));

	/// <summary>Where every card belongs now, face up or down.</summary>
	private IEnumerable<(int Id, Transform3D At)> Places()
	{
		var game = _game!;
		// The stock in one stack per deal left, the next deal on the right.
		for (int i = 0; i < game.Stock.Count; i++)
		{
			int deal = i / Spider.Piles;
			yield return (game.Stock[i], Dealer.InStack(StockAt + new Vector3((deal - game.DealsLeft + 1) * 0.14f, 0, 0), i));
		}
		for (int d = 0; d < game.Done.Count; d++)
			for (int i = 0; i < 13; i++)
				yield return (game.Done[d][12 - i], FaceUpAt(Lifted(DoneAt + new Vector3(d * 0.16f, 0, 0), d * 13 + i)));
		for (int p = 0; p < Spider.Piles; p++)
		{
			var pile = game.Pile(p);
			int down = pile.Count(c => !c.Up), up = pile.Count - down;
			float step = UpStep;
			float room = Bottom - TableauZ - down * DownStep;
			if (up > 1 && (up - 1) * step > room)
				step = room / (up - 1);
			float z = TableauZ;
			for (int i = 0; i < pile.Count; i++)
			{
				var at = Lifted(new Vector3(X(p), CardTable.FeltY, z), i);
				yield return (pile[i].Id, pile[i].Up ? FaceUpAt(at) : FaceDownAt(at));
				z += pile[i].Up ? step : DownStep;
			}
		}
	}

	/// <summary>Moves every card that isn't where it belongs, a little apart. Returns the last flight.</summary>
	private Tween? Layout(float seconds = 0.24f, float apart = 0.03f)
	{
		Tween? last = null;
		float delay = 0;
		foreach (var (id, at) in Places())
		{
			var node = _nodes[id];
			if (node.GlobalTransform.IsEqualApprox(at))
				continue;
			bool turning = node.GlobalTransform.Basis.Z.Dot(at.Basis.Z) < 0;
			last = CardMotion.Fly(node, at, seconds, lift: turning ? 0.25f : 0.14f, delay: delay, landed: () => Sounds.PlaySlap(at.Origin));
			delay += apart;
		}
		return last;
	}

	/// <summary>Faint outlines on the felt where piles go, so an empty one still shows.</summary>
	private void BuildSlots()
	{
		if (_slots != null)
			return;
		_slots = new Node3D();
		AddChild(_slots);
		var material = new StandardMaterial3D
		{
			AlbedoColor = new Color(1, 0.95f, 0.85f, 0.1f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		void Slot(Vector3 at)
		{
			_slots.AddChild(new MeshInstance3D
			{
				Mesh = new PlaneMesh { Size = new Vector2(Card3D.Width, Card3D.Height) },
				Position = at + Vector3.Up * 0.0006f,
				MaterialOverride = material,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		}
		Slot(StockAt);
		Slot(DoneAt);
		for (int p = 0; p < Spider.Piles; p++)
			Slot(new Vector3(X(p), CardTable.FeltY, TableauZ));
	}

	// ------------------------------------------------------------------ A deal

	protected override async Task NewRound()
	{
		int round = Round;
		_game = null;
		_counted = false;
		_actions.Clear();
		_undo.Visible = _hint.Visible = false;
		BuildSlots();
		SetScore();
		var pack = Spider.Pack(Suits, Random);
		var nodes = await ShuffleOnTable(pack);
		if (round != Round)
			return;
		for (int i = 0; i < pack.Count; i++)
			_nodes[i] = nodes[i];
		var game = new Spider(pack);

		// Square the pack up on the stock's place, then deal round the piles, face down.
		Hud.SetStatus("Dealing…", Muted);
		Tween? last = null;
		for (int i = 0; i < nodes.Count; i++)
			last = CardMotion.Slide(nodes[i], Dealer.InStack(StockAt, nodes.Count - 1 - i), 0.3f);
		await Await(last);
		for (int k = 0; k < 54; k++)
		{
			int pile = k % Spider.Piles, row = k / Spider.Piles;
			var at = Lifted(new Vector3(X(pile), CardTable.FeltY, TableauZ + row * DownStep), row);
			last = Dealer.Deal(_nodes[k], at - Vector3.Up * Dealer.CardThickness, 0, delay: k * 0.04f);
		}
		await Await(last);
		if (round != Round)
			return;
		_game = game;
		await Await(Layout(0.3f));
		if (round != Round)
			return;
		Hud.SetRestartVisible(true);
		_undo.Visible = _hint.Visible = !Demo;
		await AfterMove();
	}

	/// <summary>After anything changes: a win, stuck, or your turn again (or the demo's next move).</summary>
	private async Task AfterMove()
	{
		var game = _game!;
		int round = Round;
		Busy = true;
		SetScore();
		if (game.IsWon)
		{
			if (!Demo)
			{
				Settings.Extra["won"] = (Count("won") + 1).ToString();
				Settings.Save();
			}
			SetScore();
			_undo.Visible = _hint.Visible = false;
			var kings = game.Done.Select(d => _nodes[d[0]]);
			Finish($"You win! Score {game.Score}.", 0, kings, DoneAt + new Vector3(0.56f, 0, 0));
			return;
		}
		if (game.Stuck)
		{
			Hud.SetStatus("No moves left. Undo, or deal again.", Bright);
			Hud.SetNewGameVisible(true);
		}
		else if (!game.CanDeal && game.Stock.Count > 0 && Enumerable.Range(0, Spider.Piles).Any(p => game.Pile(p).Count == 0))
			Hud.SetStatus("Fill the empty piles to deal again.", Muted);
		else
			Hud.SetStatus("Your move.", Muted);
		if (Demo)
		{
			await Pause(0.3f);
			if (round == Round)
				await DemoStep();
			return;
		}
		Busy = false;
	}

	private async Task DemoStep()
	{
		var game = _game!;
		if (game.Moves > 1500)
			Finish("The computer gives up on this one.", -1);
		else if (game.Suggest() is { } move)
			await Do(move);
		else if (game.CanDeal)
			await Deal();
		else
			Finish("No moves left this time.", -1);
	}

	private void Counted()
	{
		if (_counted || Demo)
			return;
		_counted = true;
		Settings.Extra["played"] = (Count("played") + 1).ToString();
		Settings.Save();
	}

	/// <summary>Makes a move on the table, and shows it.</summary>
	private async Task Do(Move move)
	{
		var game = _game!;
		Busy = true;
		Counted();
		_actions.Push(game.History);
		var done = game.Make(move);
		await Show(done.Count > 0 ? SuitOff() : null);
	}

	/// <summary>Deals a card onto every pile.</summary>
	private async Task Deal()
	{
		var game = _game!;
		Busy = true;
		Counted();
		_actions.Push(game.History);
		var done = game.Deal();
		Sounds.PlaySwish(StockAt);
		await Await(Layout(0.28f, 0.07f));
		await Show(done.Count > 0 ? SuitOff() : null);
	}

	private string SuitOff()
	{
		var last = _game!.Done[^1];
		return $"A full suit of {_game.CardOf(last[0]).Suit.ToString().ToLowerInvariant()} comes off!";
	}

	/// <summary>Lays the cards out as the game now stands, then carries on.</summary>
	private async Task Show(string? status)
	{
		int round = Round;
		Busy = true;
		Sounds.PlaySwish(Table.Centre);
		await Await(Layout());
		if (status != null)
		{
			Hud.SetStatus(status, Bright);
			await Pause(0.8f);
		}
		if (round == Round)
			await AfterMove();
	}

	private async Task ShowHint()
	{
		var game = _game!;
		var move = game.Suggest();
		if (move == null && !game.CanDeal)
		{
			Hud.SetStatus(game.Stuck ? "No moves left. Undo, or deal again." : "Nothing useful to do: try moving a card into the empty pile.", Bright);
			return;
		}
		Busy = true;
		List<int> ids;
		if (move is { } m)
		{
			var pile = game.Pile(m.From);
			ids = pile.Skip(pile.Count - m.Count).Select(c => c.Id).ToList();
			Hud.SetStatus($"Try moving the {Describe(game.CardOf(ids[0]))}.", Bright);
		}
		else
		{
			ids = game.Stock.Skip(game.Stock.Count - Spider.Piles).ToList();
			Hud.SetStatus("Deal from the stock.", Bright);
		}
		Tween? last = null;
		foreach (var node in ids.Select(id => _nodes[id]))
			last = CardMotion.Slide(node, new Transform3D(node.GlobalTransform.Basis, node.GlobalPosition + Vector3.Up * 0.25f), 0.18f);
		await Await(last);
		await Pause(0.45f);
		await Await(Layout(0.18f, 0));
		Busy = false;
	}

	private static string Describe(Card card) => $"{Card.RankName(card.Rank).ToLowerInvariant()} of {card.Suit.ToString().ToLowerInvariant()}";

	private int Count(string key) => Settings.Extra.TryGetValue(key, out var n) && int.TryParse(n, out int v) ? v : 0;

	private void SetScore()
	{
		string record = $"Won {Count("won")} of {Count("played")}";
		Hud.SetScore(_game == null ? record : $"Score {_game.Score}  ·  Moves {_game.Moves}  ·  {record}");
	}

	// ------------------------------------------------------------------ Taps

	protected override void Tapped(Vector3 origin, Vector3 direction, Vector2 screen)
	{
		if (_game is not { IsWon: false } game)
			return;
		Hud.HideHint();
		var node = HitCard(_nodes.Where(n => n != null), origin, direction);
		if (node == null)
			return;
		int id = System.Array.IndexOf(_nodes, node);
		if (game.Stock.Contains(id))
		{
			if (game.CanDeal)
				Run(Deal());
			else
				Hud.SetStatus("Fill the empty piles before dealing.", Bright);
			return;
		}
		for (int p = 0; p < Spider.Piles; p++)
		{
			var pile = game.Pile(p);
			int index = pile.Select(c => c.Id).ToList().IndexOf(id);
			if (index < 0)
				continue;
			if (!pile[index].Up)
				return;
			int count = pile.Count - index;
			if (game.BestMove(p, count) is { } move)
				Run(Do(move));
			else
				Run(Shake(node, count > game.RunLength(p) ? "Only a run of one suit moves together." : $"The {Describe(node.Card)} has nowhere to go."));
			return;
		}
	}

	/// <summary>A little shake: that card can't move.</summary>
	private async Task Shake(Card3D node, string why)
	{
		Busy = true;
		Hud.SetStatus(why, Muted);
		var home = node.GlobalTransform;
		var tween = node.CreateTween();
		foreach (float dx in new[] { 0.05f, -0.05f, 0.03f, 0f })
			tween.TweenProperty(node, "global_position", home.Origin + new Vector3(dx, 0.02f, 0), 0.05f);
		await Await(tween);
		node.GlobalTransform = home;
		Busy = false;
	}
}
