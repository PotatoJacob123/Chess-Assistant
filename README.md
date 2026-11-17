# Chess Assistant

A Windows desktop app that captures a chessboard from the screen, recognizes its pieces, asks Stockfish for a move, and draws that move as an overlay arrow.

## Requirements

- Windows
- .NET 10 SDK
- Python available as `python` on `PATH`
- The Python package used by the recognition script:

  ```powershell
  python -m pip install chessimg2pos
  ```

Stockfish and the ONNX recognition model are included in the project. The app starts Stockfish from `Engine/Stockfish/stockfish.exe` and calls `Recognition/Python/recognize_board.py` for image recognition.

## Build and run

From the directory containing `ChessAssistant.csproj`:

```powershell
dotnet restore
dotnet build
dotnet run
```

The first recognition may take longer while Python loads its model. If the app cannot start recognition, check that `python` works from PowerShell and that `chessimg2pos` is installed for that Python interpreter.

## Use the app

1. Open **Select Chessboard** and drag a square selection tightly around the board on screen. The selector covers the virtual desktop, including other monitors. The selection is used to calibrate the board automatically.
2. Set **Board View** to match the board on screen: White at Bottom or Black at Bottom.
3. Choose **White to Move** or **Black to Move** when you need to set the side to move manually.
4. Choose **Start / Stop Live Analysis** to monitor for changes and show Stockfish's suggested move as an arrow. Click it again to stop monitoring.

The board capture, cropped image, and calibration file are stored in `%USERPROFILE%\Pictures\ChessAssistant`.

The board-view setting controls how chess coordinates are mapped to the captured screen. For a Black-at-bottom view, the recognized piece placement is rotated into standard chess coordinates before it is sent to Stockfish; the resulting move is mapped back onto the rotated screen when the arrow is drawn.

## Other controls

- **Recognize Pieces** performs recognition and analysis for the current capture.
- Stockfish's evaluation shows whether White or Black is ahead, with the score in approximate pawn units. Mate scores are shown when Stockfish finds a forced mate.
- **Clear Arrow** removes the current arrow.
- **Preview Board Crop** opens the latest cropped board image.
- The **Overlay Settings** checkboxes are currently visual controls; they are not yet connected to overlay behavior.

## Known limitations

- Live monitoring infers the next side to move by toggling after each detected board change. If two moves happen between successful recognition scans, such as a fast bot reply, the app can miss the intermediate position and infer the wrong side to move.
- Board calibration currently uses the bounding rectangle of the four selected corners to crop the captured image. It does not perform perspective correction for a tilted board.
- Recognition accuracy depends on image quality, board visibility, and the recognition model. Make sure the selected board is unobstructed and the calibration covers the board squares cleanly.
