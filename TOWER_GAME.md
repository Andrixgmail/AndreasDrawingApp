# Tower Game (Tower of Hanoi)

A second mode in Andrea's Drawing App. Move the whole stack of discs from the left peg to the right peg using your hand.

## Switching modes
Use the **Mode** buttons at the top of the control panel: **Draw** or **Tower Game**. Start the camera as usual.

## Rules
- Only the top disc of a tower can be lifted.
- A bigger disc can never be placed on a smaller one.
- Move every disc to the right-hand peg (GOAL) to win.

## Levels
- Level 1: 3 discs (best possible: 7 moves)
- Level 2: 4 discs (best possible: 15 moves)

Use the Level buttons or Restart Level in the panel. After solving a level, hold an open palm for 1.5 seconds to go to the next one.

## Hand controls
1. Move your hand: a ring follows the point between your thumb and index finger.
2. Pinch your thumb and index finger together over a tower to lift its top disc.
3. Move over another peg while still pinching.
4. Open your fingers to drop the disc.

If tracking is lost for under a second the disc stays in your hand. After that it goes back to its peg.

## Mouse fallback
You can also play by holding the left mouse button over a tower, moving, and releasing. This is handy for testing without a camera.

## Tuning the pinch
In `Services/PinchDetector.cs`, `CloseRatio` (default 0.30) controls how close the fingertips must get to start a pinch, and `OpenRatio` (default 0.50) controls how far apart to release it. Raise `CloseRatio` if pinching feels too hard, lower it if discs lift by accident.

## Adding levels
Add a number to `HanoiGame.LevelDiscCounts` in `Models/HanoiGame.cs` and a button in `MainWindow.xaml`.
