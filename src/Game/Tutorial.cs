using System.Collections.Generic;
using Renegade.Ui;

namespace SpiderSolitaire.Game;

/// <summary>The guided tour behind the Demo button: a few short pages.</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("The goal",
			"Spider uses two packs. Build each suit from king down to ace on the piles: a full suit comes off by itself. Take all eight off to win."),
		new TutorialPage("Moving",
			"Any card can go on a card one higher, whatever the suit. But only a run of one suit moves together, so build in suit when you can. Tap a card to send it, and the cards on it, to the best place."),
		new TutorialPage("Dealing",
			"Tap the stock, top right, to deal a card onto every pile. You can't deal while a pile is empty: put any card or run in it first."),
		new TutorialPage("Help",
			"Undo is free and goes back as far as you like; Hint shows a good move. New to Spider? Start with one suit (Settings › Suits); two and four suits are much harder."),
	};
}
