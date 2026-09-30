With the room open as the **Leader** on the Switch, TradeRock looks for the room and tries to
**decrypt** it, without joining. It listens for up to 30 seconds and stops as soon as it finds the
room. The result tells you what is working:

- **Room found and decrypted:** radio and keys confirmed. Trading is unlocked.
- **Room found, but not decrypted:** the keys (prod.keys) are not right.
- **No room found:** the Switch is too far, the room is not open, or something is wrong with the
  radio. Move the Switch closer, check that you are the Leader and try again.
- **Only another game found:** the keys work, but the FireRed/LeafGreen room did not show up.
  Check that you are still on the Direct Corner waiting screen.

The test is valid for this card and this prod.keys. If you change either of them, run the test
again.
