using System;

namespace SpiderSolitaire.Core;

/// <summary>The computer playing Spider its way (<see cref="Spider.Suggest"/>), for the demo: it wins most one-suit deals.</summary>
public static class Autoplay
{
	/// <summary>Plays a deal to the end; returns whether it was won.</summary>
	public static bool Play(Spider game, int limit = 1500)
	{
		for (int n = 0; n < limit && !game.IsWon; n++)
		{
			if (game.Suggest() is { } move)
				game.Make(move);
			else if (game.CanDeal)
				game.Deal();
			else
				return false;
		}
		return game.IsWon;
	}
}
