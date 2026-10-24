namespace AndreasDrawingApp.Models
{
    public enum GestureType
    {
        /// <summary>No hand detected, or an unrecognized pose.</summary>
        None,

        /// <summary>Index finger extended, middle finger down -> actively drawing.</summary>
        Drawing,

        /// <summary>Index + middle fingers extended -> move cursor without drawing.</summary>
        Hover,

        /// <summary>All four fingers extended (open palm) -> held to clear the canvas.</summary>
        OpenPalm
    }
}
