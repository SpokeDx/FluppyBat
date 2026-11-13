namespace SQFLUPPY2.Models
{
    public class Squirrel
    {
        public float X { get; set; } = 80f;
        public float Y { get; set; } = 100f;
        public float Velocity { get; set; } = 0f;
        public float Gravity { get; set; } = 0.10f;      // Замедленная гравитация
        public float GlideVelocity { get; set; } = 2.0f; // Максимальная скорость падения
        public float JumpForce { get; set; } = -3.0f;    // Сила взмаха/планирования
        public float Width { get; set; } = 42f;
        public float Height { get; set; } = 32f;

        public void Update()
        {
            Velocity += Gravity;
            if (Velocity > GlideVelocity) Velocity = GlideVelocity;
            Y += Velocity;
        }

        public void Jump() => Velocity = JumpForce;

        public void Reset()
        {
            Y = 250f;
            Velocity = 0f;
        }
        
    }
}