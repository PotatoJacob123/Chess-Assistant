
namespace ChessAssistant.Chess;

public static class FenBuilder
{
    public static string Create(
        string piecePlacement,
        char sideToMove,
        string castlingRights,
        string enPassant,
        int halfmoveClock,
        int fullmoveNumber)
    {
        if (sideToMove != 'w' && sideToMove != 'b')
            throw new ArgumentException("Side to move must be w or b.");

        if (halfmoveClock < 0 || fullmoveNumber < 1)
            throw new ArgumentException("Invalid move counters.");

        return $"{piecePlacement} {sideToMove} " +
               $"{castlingRights} {enPassant} " +
               $"{halfmoveClock} {fullmoveNumber}";
    }
}
