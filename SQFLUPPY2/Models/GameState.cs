using SQFLUPPY2.Models;
using System.Collections.Generic;

namespace FlappySquirrel.Models 
{
    public class GameState
    {
        public Squirrel Player { get; set; } = new Squirrel();

        public List<Tree> Trees { get; set; } = [];

        public int Score { get; set; } = 0;
        public bool IsGameOver { get; set; } = false;
        public float CanvasWidth { get; set; } = 400f;
        public float CanvasHeight { get; set; } = 600f;

        public void UpdatePhysics()
        {
            if (IsGameOver) return;

            Player.Update();

            if (Player.Y <= 0 || Player.Y + Player.Height >= CanvasHeight - 60)
            {
                IsGameOver = true;
                return;
            }

            for (int i = Trees.Count - 1; i >= 0; i--)
            {
                var tree = Trees[i];
                tree.Move(1.4f);

                if (!tree.Passed && tree.X + tree.Width < Player.X)
                {
                    tree.Passed = true;
                    Score++;
                }

                if (tree.X + tree.Width < 0)
                {
                    Trees.RemoveAt(i);
                }

                if (CheckCollision(Player, tree))
                {
                    IsGameOver = true;
                }
            }

            if (Trees.Count == 0 || CanvasWidth - Trees[^1].X >= 240)
            {
                Trees.Add(new Tree(CanvasWidth, CanvasHeight));
            }
        }

        private static bool CheckCollision(Squirrel s, Tree t)
        {
            bool hitX = s.X + s.Width > t.X && s.X < t.X + t.Width;
            bool hitTopTree = s.Y < t.TopHeight;
            bool hitBottomTree = s.Y + s.Height > t.BottomY;

            return hitX && (hitTopTree || hitBottomTree);
        }
    }
}