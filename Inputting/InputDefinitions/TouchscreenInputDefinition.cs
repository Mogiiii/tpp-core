using Inputting.Inputs;

namespace Inputting.InputDefinitions
{
    /// <summary>
    /// This input definition creates inputs of type <see cref="TouchscreenInput"/>.
    /// </summary>
    public readonly struct TouchscreenInputDefinition : IInputDefinition
    {
        private readonly string _touchscreenName;
        private readonly uint _width;
        private readonly uint _height;

        public TouchscreenInputDefinition(string touchscreenName, uint width, uint height)
        {
            _touchscreenName = touchscreenName;
            _width = width;
            _height = height;
        }

        private const string Number = @"(?:[0-9]{1,4})";
        public string InputRegex => $@"{Number},{Number}";

        public Input? Parse(string str)
        {
            string[] split = str.Split(',', count: 2);
            (uint x, uint y) = (uint.Parse(split[0]), uint.Parse(split[1]));
            if (x >= _width || y >= _height)
            {
                return null;
            }
            return new TouchscreenInput(str, _touchscreenName, str, x, y);
        }
    }
}
