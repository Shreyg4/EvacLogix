namespace EvacLogix.Sandbox.Data
{
    public static class SandboxDistanceUnitUtility
    {
        public static string GetLabel(DistanceUnit unit)
        {
            return unit switch
            {
                DistanceUnit.Feet => "Feet",
                DistanceUnit.Meters => "Meters",
                DistanceUnit.Inches => "Inches",
                DistanceUnit.Centimeters => "Centimeters",
                _ => "Feet",
            };
        }

        public static string GetAbbreviation(DistanceUnit unit)
        {
            return unit switch
            {
                DistanceUnit.Feet => "ft",
                DistanceUnit.Meters => "m",
                DistanceUnit.Inches => "in",
                DistanceUnit.Centimeters => "cm",
                _ => "ft",
            };
        }

        public static string FormatDistance(float value, DistanceUnit unit, string format = "0.##")
        {
            return $"{value.ToString(format, System.Globalization.CultureInfo.InvariantCulture)} {GetAbbreviation(unit)}";
        }

        public static string FormatArea(float value, DistanceUnit unit, string format = "0.##")
        {
            return $"{value.ToString(format, System.Globalization.CultureInfo.InvariantCulture)} sq {GetAbbreviation(unit)}";
        }

        public static DistanceUnit Normalize(DistanceUnit unit)
        {
            return unit is DistanceUnit.Feet or DistanceUnit.Meters or DistanceUnit.Inches or DistanceUnit.Centimeters
                ? unit
                : DistanceUnit.Feet;
        }
    }
}
