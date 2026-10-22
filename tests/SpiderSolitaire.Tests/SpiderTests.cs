using System;
using System.Linq;
using Renegade.Cards;
using SpiderSolitaire.Core;
using Xunit;

namespace SpiderSolitaire.Tests;

public class SpiderTests
{
	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(4)]
	public void TwoPacksOfOneTwoOrFourSuits(int suits)
	{
		var pack = Spider.Pack(suits, new Random(1));
		Assert.Equal(104, pack.Count);
		Assert.Equal(suits, pack.Select(c => c.Suit).Distinct().Count());
		Assert.All(pack.GroupBy(c => c), g => Assert.Equal(8 / suits, g.Count()));
	}

	[Fact]
	public void TenPilesOfSixAndFiveWithFiftyInTheStock()
	{
		var game = new Spider(Spider.Pack(4, new Random(2)));
		for (int p = 0; p < Spider.Piles; p++)
		{
			Assert.Equal(p < 4 ? 6 : 5, game.Pile(p).Count);
			Assert.True(game.Pile(p)[^1].Up);
			Assert.Equal(1, game.Pile(p).Count(c => c.Up));
		}
		Assert.Equal(50, game.Stock.Count);
		Assert.Equal(5, game.DealsLeft);
	}

	[Fact]
	public void BuildDownInAnySuitButOnlyMoveRunsOfOneSuit()
	{
		for (int seed = 0; seed < 20; seed++)
		{
			var game = new Spider(Spider.Pack(4, new Random(seed)));
			for (int from = 0; from < Spider.Piles; from++)
				for (int to = 0; to < Spider.Piles; to++)
				{
					var a = game.CardOf(game.Pile(from)[^1].Id);
					var b = game.CardOf(game.Pile(to)[^1].Id);
					Assert.Equal(from != to && b.Rank == a.Rank + 1, game.IsLegal(new Move(from, to, 1)));
					Assert.False(game.IsLegal(new Move(from, to, 2))); // the card under is face down
				}
		}
	}

	[Fact]
	public void DealingNeedsEveryPileFilled()
	{
		var game = new Spider(Spider.Pack(1, new Random(3)));
		Assert.True(game.CanDeal);
		game.Deal();
		Assert.Equal(40, game.Stock.Count);
		Assert.All(Enumerable.Range(0, Spider.Piles), p => Assert.True(game.Pile(p)[^1].Up));
		// Empty a pile by moving cards off it (one suit: lots of moves); then no dealing.
		for (int n = 0; n < 500 && game.Pile(9).Count > 0; n++)
		{
			int run = game.RunLength(9);
			var move = game.BestMove(9, run);
			if (move == null)
				break;
			game.Make(move.Value);
		}
		if (game.Pile(9).Count == 0)
			Assert.False(game.CanDeal);
	}

	[Fact]
	public void AFullSuitIsTakenOffAndScoresAHundred()
	{
		// Play one-suit deals until a suit comes off, and check it.
		for (int seed = 0; seed < 20; seed++)
		{
			var game = new Spider(Spider.Pack(1, new Random(seed)));
			for (int n = 0; n < 1500 && game.Done.Count == 0; n++)
			{
				if (game.Suggest() is { } move)
					game.Make(move);
				else if (game.CanDeal)
					game.Deal();
				else
					break;
			}
			if (game.Done.Count == 0)
				continue;
			var run = game.Done[0].Select(game.CardOf).ToList();
			Assert.Equal(Enumerable.Range(1, 13).Reverse(), run.Select(c => c.Rank));
			Assert.Single(run.Select(c => c.Suit).Distinct());
			Assert.Equal(500 - game.Moves + 100, game.Score);
			return;
		}
		Assert.Fail("No suit came off.");
	}

	[Fact]
	public void UndoPutsEverythingBack()
	{
		var game = new Spider(Spider.Pack(2, new Random(4)));
		var before = Enumerable.Range(0, Spider.Piles).Select(p => game.Pile(p).ToList()).ToList();
		game.Deal();
		if (game.Suggest() is { } move)
			game.Make(move);
		while (game.CanUndo)
			game.Undo();
		Assert.Equal(50, game.Stock.Count);
		Assert.Equal(500, game.Score);
		for (int p = 0; p < Spider.Piles; p++)
			Assert.Equal(before[p], game.Pile(p).ToList());
	}

	[Fact]
	public void TheComputerWinsSomeOneSuitDealsAndNeverLosesACard()
	{
		int won = 0;
		for (int seed = 0; seed < 40; seed++)
		{
			var game = new Spider(Spider.Pack(1, new Random(seed)));
			if (Autoplay.Play(game))
				won++;
			int cards = Enumerable.Range(0, Spider.Piles).Sum(p => game.Pile(p).Count) + game.Stock.Count + game.Done.Count * 13;
			Assert.Equal(104, cards);
			var ids = Enumerable.Range(0, Spider.Piles).SelectMany(p => game.Pile(p).Select(c => c.Id)).Concat(game.Stock).Concat(game.Done.SelectMany(d => d));
			Assert.Equal(104, ids.Distinct().Count());
		}
		Assert.InRange(won, 25, 40);
	}
}
