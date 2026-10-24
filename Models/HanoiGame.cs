using System;
using System.Collections.Generic;

namespace AndreasDrawingApp.Models
{
    public enum DropResult
    {
        None,
        Moved,
        Returned,
        Illegal,
        Solved
    }

    /// <summary>
    /// Pure game logic for Tower of Hanoi. No UI or camera code in here.
    /// Discs are numbers: 1 is the smallest, DiscCount is the largest.
    /// Each peg is a list ordered bottom to top, so the last item is the disc you can lift.
    /// The goal is to move the whole tower from the first peg to the last peg.
    /// </summary>
    public class HanoiGame
    {
        public const int PegCount = 3;

        /// <summary>Number of discs for each level. Add a number here to add a level.</summary>
        public static readonly int[] LevelDiscCounts = { 3, 4 };

        public static int MaxDiscCount
        {
            get
            {
                int max = 0;
                foreach (int n in LevelDiscCounts) max = Math.Max(max, n);
                return max;
            }
        }

        private readonly List<int>[] _pegs = new List<int>[PegCount];

        public int LevelIndex { get; private set; }
        public int DiscCount => LevelDiscCounts[LevelIndex];
        public int Moves { get; private set; }
        public int OptimalMoves => (1 << DiscCount) - 1;

        /// <summary>The disc currently being carried by the hand, if any.</summary>
        public int? HeldDisc { get; private set; }
        public int HeldFromPeg { get; private set; } = -1;

        public bool IsSolved { get; private set; }
        public bool HasNextLevel => LevelIndex < LevelDiscCounts.Length - 1;
        public string Message { get; private set; } = "";

        public IReadOnlyList<int> GetPeg(int index) => _pegs[index];

        public HanoiGame()
        {
            StartLevel(0);
        }

        public void StartLevel(int levelIndex)
        {
            LevelIndex = Math.Clamp(levelIndex, 0, LevelDiscCounts.Length - 1);

            for (int i = 0; i < PegCount; i++)
                _pegs[i] = new List<int>();

            for (int size = DiscCount; size >= 1; size--)
                _pegs[0].Add(size);

            Moves = 0;
            HeldDisc = null;
            HeldFromPeg = -1;
            IsSolved = false;
            Message = "Pinch your thumb and index finger over a tower to lift its top disc.";
        }

        public void Restart() => StartLevel(LevelIndex);

        /// <summary>Goes to the next level, or back to level 1 after the last one.</summary>
        public void AdvanceAfterSolved() => StartLevel(HasNextLevel ? LevelIndex + 1 : 0);

        public bool TryPickUp(int peg)
        {
            if (IsSolved || HeldDisc != null) return false;
            if (peg < 0 || peg >= PegCount) return false;

            var list = _pegs[peg];
            if (list.Count == 0)
            {
                Message = "That peg is empty. Pinch over a peg that has discs.";
                return false;
            }

            HeldDisc = list[^1];
            list.RemoveAt(list.Count - 1);
            HeldFromPeg = peg;
            Message = "Move over a peg and open your fingers to drop the disc.";
            return true;
        }

        public DropResult Drop(int peg)
        {
            if (HeldDisc is not int disc) return DropResult.None;

            int from = HeldFromPeg;
            HeldDisc = null;
            HeldFromPeg = -1;

            if (peg < 0 || peg >= PegCount) peg = from;

            var target = _pegs[peg];

            if (target.Count > 0 && target[^1] < disc)
            {
                _pegs[from].Add(disc);
                Message = "A bigger disc cannot sit on a smaller one. The disc went back.";
                return DropResult.Illegal;
            }

            target.Add(disc);

            if (peg == from)
            {
                Message = "Disc put back. Pinch again to lift a disc.";
                return DropResult.Returned;
            }

            Moves++;

            if (_pegs[PegCount - 1].Count == DiscCount)
            {
                IsSolved = true;
                Message = $"Solved in {Moves} moves (best possible is {OptimalMoves}).";
                return DropResult.Solved;
            }

            Message = "Nice move. Keep going until every disc is on the right-hand peg.";
            return DropResult.Moved;
        }

        /// <summary>Puts a carried disc back where it came from (for example if the hand is lost).</summary>
        public void CancelHold()
        {
            if (HeldDisc is int disc && HeldFromPeg >= 0)
            {
                _pegs[HeldFromPeg].Add(disc);
                HeldDisc = null;
                HeldFromPeg = -1;
                Message = "Lost sight of your hand, so the disc went back.";
            }
        }
    }
}
