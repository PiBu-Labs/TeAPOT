// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using System;

namespace Teapot
{
    /// <summary>
    /// Struct to hold geoposition with heading information.
    /// </summary>
    [Serializable]
    public struct GeopositionHeading
    {
        public DateTimeOffset Timestamp;
        public double Latitude;
        public double Longitude;
        public double Heading;

        public GeopositionHeading(DateTimeOffset timestamp, double latitude, double longitude, double heading)
        {
            Timestamp = timestamp;
            Latitude = latitude;
            Longitude = longitude;
            Heading = heading;
        }
    }
}
