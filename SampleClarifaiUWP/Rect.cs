namespace SampleClarifaiUWP
{
    public class Rect
    {
        public double Top { get; }
        public double Left { get; }
        public double Width { get; }
        public double Height { get; }

        public Rect(double top, double left, double width, double height)
        {
            Top = top;
            Left = left;
            Width = width;
            Height = height;
        }
    }
}
