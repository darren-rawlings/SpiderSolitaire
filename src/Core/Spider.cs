using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace SpiderSolitaire.Core;

/// <summary>A card in a pile, by its place in the two-deck pack (0–103), and whether it's face up.</summary>
public readonly record struct Placed(int Id, bool Up);

/// <summary>A move of the top cards of one pile onto another.</summary>
public readonly record struct Move(int From, int To, int Count);

/// <summary>
/// Spider solitaire with two packs (104 cards) of one, two or four suits. Ten piles: the first
/// four get six cards, the rest five, the top card of each face up; the other 50 are the stock,
/// dealt ten at a time, one face up on every pile, but only when no pile is empty. Build down
/// regardless of suit; move a run together only if it's all one suit; any card or run may fill
/// an empty pile. A full suit from king down to ace is taken off; take off all eight to win.
/// A face-down card left on top turns face up. Scoring: 500 to start, a point off for every
/// move and 100 for every suit taken off.
/// </summary>
public sealed class Spider
{
	public const int Piles = 10;

	private readonly Card[] _cards;
	private readonly List<Placed>[] _piles = Enumerable.Range(0, Piles).Select(_ => new List<Placed>()).ToArray();
	private readonly List<int> _stock = new();
	private readonly List<List<int>> _done = new();
	private readonly Stack<Snapshot> _undo = new();
	private readonly HashSet<long> _seen = new();
	private bool _thinking;

	/// <summary>The best position reached since the last deal: the computer only heads for better.</summary>
	private double _peak = double.MinValue;

	private sealed record Snapshot(List<Placed>[] Piles, List<int> Stock, List<List<int>> Done, int Moves, int Score);

	/// <summary>
	/// Deals a 104-card pack (top first) one card at a time round the ten piles; the first 54
	/// make the piles, the rest is the stock.
	/// </summary>
	public Spider(IReadOnlyList<Card> pack)
	{
		if (pack.Count != 104)
			throw new ArgumentException("Spider needs two packs.", nameof(pack));
		_cards = pack.ToArray();
		for (int k = 0; k < 54; k++)
			_piles[k % Piles].Add(new Placed(k, k >= 44));
		for (int k = 103; k >= 54; k--)
			_stock.Add(k);
	}

	/// <summary>Two packs' worth of cards in one, two or four suits (spades, then hearts, then the rest), shuffled.</summary>
	public static List<Card> Pack(int suits, Random random)
	{
		var order = suits switch
		{
			1 => new[] { Suit.Spades },
			2 => new[] { Suit.Spades, Suit.Hearts },
			_ => new[] { Suit.Spades, Suit.Hearts, Suit.Diamonds, Suit.Clubs },
		};
		var pack = new List<Card>();
		for (int copy = 0; copy < 8 / order.Length; copy++)
			foreach (var suit in order)
				for (int rank = 1; rank <= 13; rank++)
					pack.Add(new Card(rank, suit));
		Deck.Shuffle(pack, random);
		return pack;
	}

	public int Moves { get; private set; }
	public int Score { get; private set; } = 500;

	public Card CardOf(int id) => _cards[id];
	public IReadOnlyList<Placed> Pile(int p) => _piles[p];

	/// <summary>The stock, bottom first (the last ten are the next deal).</summary>
	public IReadOnlyList<int> Stock => _stock;

	/// <summary>The suits taken off, each king first.</summary>
	public IReadOnlyList<IReadOnlyList<int>> Done => _done;

	public int DealsLeft => _stock.Count / Piles;
	public bool IsWon => _done.Count == 8;
	public bool CanUndo => _undo.Count > 0;
	public int History => _undo.Count;

	/// <summary>Whether the stock can be dealt: there is some, and no pile is empty.</summary>
	public bool CanDeal => _stock.Count > 0 && _piles.All(p => p.Count > 0);

	private Card Top(int p) => _cards[_piles[p][^1].Id];

	private void Save() => _undo.Push(new Snapshot(_piles.Select(p => p.ToList()).ToArray(), _stock.ToList(),
		_done.Select(d => d.ToList()).ToList(), Moves, Score));

	public void Undo()
	{
		if (_undo.Count == 0)
			return;
		var s = _undo.Pop();
		for (int p = 0; p < Piles; p++)
		{
			_piles[p].Clear();
			_piles[p].AddRange(s.Piles[p]);
		}
		_stock.Clear();
		_stock.AddRange(s.Stock);
		_done.Clear();
		_done.AddRange(s.Done);
		Moves = s.Moves;
		Score = s.Score;
	}

	/// <summary>How many cards on top of a pile form a run you can move: face up, one suit, each one lower.</summary>
	public int RunLength(int p)
	{
		var pile = _piles[p];
		if (pile.Count == 0)
			return 0;
		int n = 1;
		for (int i = pile.Count - 2; i >= 0; i--)
		{
			var below = pile[i];
			var above = _cards[pile[i + 1].Id];
			var card = _cards[below.Id];
			if (!below.Up || card.Suit != above.Suit || card.Rank != above.Rank + 1)
				break;
			n++;
		}
		return n;
	}

	public bool IsLegal(Move move)
	{
		if (move.From == move.To || move.Count < 1 || move.Count > RunLength(move.From))
			return false;
		var first = _cards[_piles[move.From][^move.Count].Id];
		return _piles[move.To].Count == 0 || Top(move.To).Rank == first.Rank + 1;
	}

	/// <summary>Makes a move. Returns the piles a full suit was taken off (at most one).</summary>
	public List<int> Make(Move move)
	{
		if (!IsLegal(move))
			throw new InvalidOperationException("That move isn't allowed.");
		Save();
		var from = _piles[move.From];
		var moving = from.Skip(from.Count - move.Count).ToList();
		from.RemoveRange(from.Count - move.Count, move.Count);
		_piles[move.To].AddRange(moving);
		TurnTop(move.From);
		Moves++;
		Score--;
		var done = new List<int>();
		if (TakeOff(move.To))
			done.Add(move.To);
		if (!_thinking)
		{
			_seen.Add(Position());
			_peak = Math.Max(_peak, Value());
		}
		return done;
	}

	/// <summary>Deals a card face up onto every pile. Returns the piles a full suit was taken off.</summary>
	public List<int> Deal()
	{
		if (!CanDeal)
			throw new InvalidOperationException("Fill every pile before dealing.");
		Save();
		for (int p = 0; p < Piles; p++)
		{
			_piles[p].Add(new Placed(_stock[^1], true));
			_stock.RemoveAt(_stock.Count - 1);
		}
		Moves++;
		Score--;
		var done = new List<int>();
		for (int p = 0; p < Piles; p++)
			if (TakeOff(p))
				done.Add(p);
		_peak = Value();
		return done;
	}

	private void TurnTop(int p)
	{
		if (_piles[p] is { Count: > 0 } pile && !pile[^1].Up)
			pile[^1] = pile[^1] with { Up = true };
	}

	/// <summary>Takes a full suit, king to ace, off the top of a pile.</summary>
	private bool TakeOff(int p)
	{
		if (RunLength(p) < 13)
			return false;
		var pile = _piles[p];
		var run = pile.Skip(pile.Count - 13).Select(c => c.Id).ToList();
		pile.RemoveRange(pile.Count - 13, 13);
		_done.Add(run);
		Score += 100;
		TurnTop(p);
		return true;
	}

	/// <summary>
	/// The best place for the cards from a position up: onto the same suit one higher, then any
	/// card one higher, then an empty pile (unless that just moves a whole pile). Null if none.
	/// </summary>
	public Move? BestMove(int from, int count)
	{
		if (count > RunLength(from))
			return null;
		var first = _cards[_piles[from][^count].Id];
		Move? any = null, empty = null;
		for (int to = 0; to < Piles; to++)
		{
			var move = new Move(from, to, count);
			if (!IsLegal(move))
				continue;
			if (_piles[to].Count == 0)
				empty ??= move;
			else if (Top(to).Suit == first.Suit)
				return move;
			else
				any ??= move;
		}
		if (any != null)
			return any;
		return count < _piles[from].Count ? empty : null;
	}

	/// <summary>
	/// How good a position looks to the computer: suits taken off, face-down cards turned, empty
	/// piles, and cards sitting on the card one higher (more if it's their own suit).
	/// </summary>
	private double Value()
	{
		double v = _done.Count * 100;
		for (int p = 0; p < Piles; p++)
		{
			var pile = _piles[p];
			if (pile.Count == 0)
			{
				v += _stock.Count > 0 ? 5 : 1;
				continue;
			}
			for (int i = 0; i < pile.Count; i++)
			{
				if (!pile[i].Up)
				{
					v -= 6;
					continue;
				}
				if (i == 0)
					continue;
				var below = _cards[pile[i - 1].Id];
				var card = _cards[pile[i].Id];
				if (pile[i - 1].Up && below.Rank == card.Rank + 1)
					v += below.Suit == card.Suit ? 2.2 : 1;
			}
		}
		return v;
	}

	/// <summary>Every move that could matter: any part of a pile's run onto a card one higher, or into a space (not a whole pile).</summary>
	private List<Move> Candidates()
	{
		var moves = new List<Move>();
		for (int from = 0; from < Piles; from++)
		{
			int run = RunLength(from);
			for (int n = 1; n <= run; n++)
			{
				var first = _cards[_piles[from][^n].Id];
				bool toSpace = false;
				for (int to = 0; to < Piles; to++)
				{
					if (to == from)
						continue;
					if (_piles[to].Count == 0)
					{
						// One space is as good as another; and moving a whole pile to one achieves nothing.
						if (!toSpace && n < _piles[from].Count)
						{
							moves.Add(new Move(from, to, n));
							toSpace = true;
						}
					}
					else if (Top(to).Rank == first.Rank + 1)
						moves.Add(new Move(from, to, n));
				}
			}
		}
		return moves;
	}

	/// <summary>
	/// A sensible next move for the computer (the demo, and hints), or null to deal: it looks two
	/// moves ahead for the best position it can reach, and makes the first move towards it if that
	/// beats where it is now. With the stock waiting on an empty pile, it fills the pile as well as it can.
	/// </summary>
	public Move? Suggest()
	{
		double now = Math.Max(Value(), _peak);
		Move? best = null, fill = null;
		double bestValue = double.MinValue, fillValue = double.MinValue;
		_thinking = true;
		try
		{
			foreach (var first in Candidates())
			{
				Make(first);
				double value = Value();
				bool seen = _seen.Contains(Position());
				if (!seen)
					foreach (var second in Candidates())
					{
						Make(second);
						if (!_seen.Contains(Position()))
							value = Math.Max(value, Value() - 0.01);
						Undo();
					}
				Undo();
				if (_piles[first.To].Count == 0 && value > fillValue)
				{
					fill = first;
					fillValue = value;
				}
				if (!seen && value > bestValue + 1e-9)
				{
					best = first;
					bestValue = value;
				}
			}
		}
		finally
		{
			_thinking = false;
		}
		if (best != null && bestValue > now + 0.05)
			return best;
		// The stock is waiting on an empty pile: fill it as well as possible.
		if (!CanDeal && _stock.Count > 0 && _piles.Any(p => p.Count == 0))
			return fill ?? best;
		return null;
	}

	/// <summary>A fingerprint of the piles, so the computer doesn't go round in circles.</summary>
	private long Position()
	{
		long hash = _stock.Count;
		foreach (var pile in _piles)
		{
			hash = hash * 1_000_003 + 7;
			foreach (var c in pile)
				hash = hash * 31 + c.Id * 2 + (c.Up ? 1 : 0);
		}
		return hash;
	}

	/// <summary>Whether any card can move at all (not counting a whole pile into a space).</summary>
	public bool HasMoves => Enumerable.Range(0, Piles).Any(from => Enumerable.Range(1, RunLength(from)).Any(n => BestMove(from, n) != null));

	/// <summary>Nothing more can be done: no card can move and nothing can be dealt.</summary>
	public bool Stuck => !IsWon && !CanDeal && !HasMoves;
}
