# Spider Solitaire: design

A Renegade card game on the card kit ([renegade-cards](https://github.com/darren-rawlings/renegade-cards)),
on its solitaire table layout (widened for ten piles with `CardGame.LayoutWidth`).

## The game
Spider with two packs (104 cards) in one, two or four suits. Ten piles (four of six cards, six of
five, the top card up); the other 50 are the stock, dealt ten at a time, one onto every pile, but
only when no pile is empty. Build down regardless of suit; only a run of one suit moves together;
any card or run fills an empty pile. A full suit from king to ace comes off by itself; take all
eight off to win. Score: 500 to start, a point off per move (deals too), 100 per suit off.
Rules in `src/Core/Spider.cs` (tested).

## The computer's play
For the demo and Hint: it looks two moves ahead, valuing suits off, face-down cards turned, empty
piles and cards sitting on the card one higher (more when in suit). It only heads for positions
better than the best since the last deal, and never returns to one it has seen, so it can't go
round in circles. It wins about 85% of one-suit deals (tested), and rarely two or four suits.

## Feel
- Both packs are shuffled in front of you, squared up on the stock and dealt round the piles.
- Tap a card: it and the cards on it fly to the best place (onto its own suit first, then any
  card one higher, then a space). A card that can't move shakes and says why.
- The stock shows one stack per deal left. Suits taken off stack up top left.
- Signature effect: the finished suits catch fire when you win.

## Help
- **Undo** is free (no ad) and goes back as far as you like.
- **Hint** lifts the cards of a good move (or the stock) and says what to try.

## Settings
- **Suits:** one (the default, for beginners), two or four.

## Record
The score line shows the score, the moves and "Won X of Y" (a deal counts once you move).

## Scope for version 1
The above. Later: drag to move, a timer, daily deals, tutorial clips, winnable deals for two and
four suits (needs a stronger solver).
