namespace SQFLUPPY2.Models
{
    public class Tree
    {
        public float X { get; set; }
        public float TopHeight { get; set; }
        public float BottomY { get; set; }
        public float Width { get; set; } = 60f;
        public float Gap { get; set; } = 160f;
        public bool Passed { get; set; } = false;

        private static float lastTopHeight = 150f;

        public Tree(float startX, float canvasHeight)
        {
            X = startX;
            var random = new Random();

            float delta = (float)(random.NextDouble() * 140 - 70);
            float newTop = lastTopHeight + delta;

            float minHeight = 50f;
            float maxHeight = canvasHeight - Gap - minHeight - 70;

            if (newTop < minHeight) newTop = minHeight;
            if (newTop > maxHeight) newTop = maxHeight;

            TopHeight = (float)(random.NextDouble() * (maxHeight - minHeight) + minHeight);
            BottomY = TopHeight + Gap;
            lastTopHeight = TopHeight;
        }

        public void Move(float speed)
        {
            X -= speed;
        }
    }
}