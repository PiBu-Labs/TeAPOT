// Copyright (c) 2026 Tania Krisanty & Victor, TU Dresden.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Teapot
{
    /// <summary>
    /// Geodetic statistics on WGS-84: mean geodetic position (lat/lon),
    /// local ENU scatter (RMS, 1σ ellipse, orientation), and circular heading stats.
    /// </summary>
    public static class GeoStats
    {
        public struct PositionSample
        {
            public double LatDeg;
            public double LonDeg;
            public double Weight;

            public PositionSample(double latDeg, double lonDeg, double weight = 1.0)
            {
                LatDeg = latDeg;
                LonDeg = lonDeg;
                Weight = weight;
            }
        }

        public struct HeadingSample
        {
            public double HeadingDeg;
            public double Weight;

            public HeadingSample(double headingDeg, double weight = 1.0)
            {
                HeadingDeg = headingDeg;
                Weight = weight;
            }
        }

        public struct PositionResult
        {
            public double MeanLatDeg;
            public double MeanLonDeg;

            // ENU scatter
            public double RmsRadiusMeters;
            public double MaxRadiusMeters;
            public double SigmaMajorMeters; // 1σ ellipse major axis (points scatter)

            public double SigmaMinorMeters; // 1σ ellipse minor axis

            // Orientation of the ellipse:
            // - from East (math convention, CCW): 0°=East, 90°=North
            // - and as a compass bearing (from North, CW)
            public double EllipseOrientationFromEastDeg;
            public double EllipseBearingFromNorthDeg;
        }

        public struct HeadingResult
        {
            public double MeanHeadingDeg; // [0,360)
            public double R; // mean resultant length (0..1), higher = tighter cluster
            public double CircularStdDeg; // circular standard deviation (deg), 0 = very tight
        }

        public struct Result
        {
            public PositionResult Position;
            public HeadingResult Heading;
        }

        public static PositionResult ComputePosition(IEnumerable<PositionSample> samples)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            var pts = samples.ToList();
            if (pts.Count == 0) throw new ArgumentException("No samples provided.");

            // Mean position via spherical/vector mean
            var (meanLat, meanLon) = SphericalMeanLatLon(pts.Select(p => (p.LatDeg, p.LonDeg, p.Weight)));

            // ENU offsets around the mean for scatter stats
            var enu = ToEnuOffsets(pts, meanLat, meanLon);
            var posStats = ComputeEnuScatter(enu);
            posStats.MeanLatDeg = meanLat;
            posStats.MeanLonDeg = meanLon;

            return posStats;
        }

        // Compute circular mean of heading
        public static HeadingResult ComputeHeading(IEnumerable<HeadingSample> samples)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            var pts = samples.ToList();
            if (pts.Count == 0) throw new ArgumentException("No samples provided.");

            return CircularMean(pts.Select(p => (p.HeadingDeg, p.Weight)));
        }

        // Spherical mean of lat/lon (robust globally)
        private static (double latDeg, double lonDeg) SphericalMeanLatLon(
            IEnumerable<(double latD, double lonD, double w)> pts)
        {
            double X = 0, Y = 0, Z = 0, W = 0;
            foreach (var (latD, lonD, w) in pts)
            {
                double lat = Deg2Rad(latD), lon = Deg2Rad(lonD);
                double cl = Math.Cos(lat);
                X += w * cl * Math.Cos(lon);
                Y += w * cl * Math.Sin(lon);
                Z += w * Math.Sin(lat);
                W += w;
            }

            if (W <= 0) throw new ArgumentException("Total weight must be > 0");
            X /= W;
            Y /= W;
            Z /= W;
            double L = Math.Sqrt(X * X + Y * Y + Z * Z);
            X /= L;
            Y /= L;
            Z /= L;
            double latMean = Math.Atan2(Z, Math.Sqrt(X * X + Y * Y));
            double lonMean = Math.Atan2(Y, X);
            return (Rad2Deg(latMean), Rad2Deg(lonMean));
        }

        // Circular mean for headings
        private static HeadingResult CircularMean(IEnumerable<(double headingDeg, double w)> hs)
        {
            double C = 0, S = 0, W = 0;
            foreach (var (hD, w) in hs)
            {
                double th = Deg2Rad(Norm360(hD));
                C += w * Math.Cos(th);
                S += w * Math.Sin(th);
                W += w;
            }

            if (W <= 0) throw new ArgumentException("Total weight must be > 0");
            double mean = Math.Atan2(S, C);
            double R = Math.Sqrt(C * C + S * S) / W;
            R = Math.Max(0.0, Math.Min(1.0, R));
            double circStdRad = (R > 0) ? Math.Sqrt(-2.0 * Math.Log(R)) : Math.PI / Math.Sqrt(3); // fallback
            return new HeadingResult
            {
                MeanHeadingDeg = Norm360(Rad2Deg(mean)),
                R = R,
                CircularStdDeg = Rad2Deg(circStdRad)
            };
        }

        // ENU scatter stats around mean
        private static List<(double E, double N, double w)> ToEnuOffsets(List<PositionSample> pts, double refLatDeg,
            double refLonDeg)
        {
            // Reference ECEF
            var refEcef = GeodeticToEcef(refLatDeg, refLonDeg, 0.0);
            // Precompute rotation for ECEF->ENU at reference
            var R = EcefToEnuRotation(refLatDeg, refLonDeg);

            var list = new List<(double E, double N, double w)>(pts.Count);
            foreach (var p in pts)
            {
                var ecef = GeodeticToEcef(p.LatDeg, p.LonDeg, 0.0);
                var dx = new double[] { ecef.X - refEcef.X, ecef.Y - refEcef.Y, ecef.Z - refEcef.Z };
                var enu = MatVec(R, dx); // [E,N,U]
                list.Add((enu[0], enu[1], p.Weight <= 0 ? 1.0 : p.Weight));
            }

            return list;
        }

        private static PositionResult ComputeEnuScatter(List<(double E, double N, double w)> enu)
        {
            double W = enu.Sum(t => t.w);
            if (W <= 0) throw new ArgumentException("Total weight must be > 0");

            // Weighted mean (should be ~0 by construction)
            double Em = enu.Sum(t => t.w * t.E) / W;
            double Nm = enu.Sum(t => t.w * t.N) / W;

            // Weighted covariance
            double sEE = 0, sNN = 0, sEN = 0, rmsSum = 0, maxR = 0;
            foreach (var (E, N, w) in enu)
            {
                double e = E - Em, n = N - Nm;
                sEE += w * e * e;
                sNN += w * n * n;
                sEN += w * e * n;
                double r = Math.Sqrt(e * e + n * n);
                rmsSum += w * r * r;
                if (r > maxR) maxR = r;
            }

            double covEE = sEE / W;
            double covNN = sNN / W;
            double covEN = sEN / W;

            // Eigen decomposition of 2x2 symmetric covariance
            var (lamMax, lamMin, thetaRad) = EigenSym2x2(covEE, covEN, covNN);
            double sigmaMajor = Math.Sqrt(Math.Max(0, lamMax));
            double sigmaMinor = Math.Sqrt(Math.Max(0, lamMin));

            double thetaDegFromEast = Rad2Deg(thetaRad); // 0°=East, CCW to North
            if (thetaDegFromEast < 0) thetaDegFromEast += 360.0;
            double bearingFromNorth = (90.0 - thetaDegFromEast);
            bearingFromNorth = (bearingFromNorth % 360 + 360) % 360;

            return new PositionResult
            {
                MeanLatDeg = double.NaN, // filled by caller
                MeanLonDeg = double.NaN, // filled by caller
                RmsRadiusMeters = Math.Sqrt(rmsSum / W),
                MaxRadiusMeters = maxR,
                SigmaMajorMeters = sigmaMajor,
                SigmaMinorMeters = sigmaMinor,
                EllipseOrientationFromEastDeg = thetaDegFromEast,
                EllipseBearingFromNorthDeg = bearingFromNorth
            };
        }

        // Math / Geo helpers

        private const double WGS84_A = 6378137.0; // semi-major axis (m)
        private const double WGS84_F = 1.0 / 298.257223563; // flattening
        private const double WGS84_E2 = WGS84_F * (2.0 - WGS84_F); // first eccentricity squared

        private struct Ecef
        {
            public double X, Y, Z;

            public Ecef(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }

        private static Ecef GeodeticToEcef(double latDeg, double lonDeg, double hMeters)
        {
            double lat = Deg2Rad(latDeg), lon = Deg2Rad(lonDeg);
            double sinφ = Math.Sin(lat), cosφ = Math.Cos(lat);
            double sinλ = Math.Sin(lon), cosλ = Math.Cos(lon);
            double N = WGS84_A / Math.Sqrt(1.0 - WGS84_E2 * sinφ * sinφ);
            double X = (N + hMeters) * cosφ * cosλ;
            double Y = (N + hMeters) * cosφ * sinλ;
            double Z = (N * (1.0 - WGS84_E2) + hMeters) * sinφ;
            return new Ecef(X, Y, Z);
        }

        // Rotation matrix R s.t. v_enu = R * (v_ecef - ref_ecef)
        private static double[][] EcefToEnuRotation(double refLatDeg, double refLonDeg)
        {
            double lat = Deg2Rad(refLatDeg), lon = Deg2Rad(refLonDeg);
            double sinφ = Math.Sin(lat), cosφ = Math.Cos(lat);
            double sinλ = Math.Sin(lon), cosλ = Math.Cos(lon);

            return new[]
            {
                new[] { -sinλ, cosλ, 0 }, // East
                new[] { -sinφ * cosλ, -sinφ * sinλ, cosφ }, // North
                new[] { cosφ * cosλ, cosφ * sinλ, sinφ } // Up
            };
        }

        private static double[] MatVec(double[][] M, double[] v)
        {
            return new[]
            {
                M[0][0] * v[0] + M[0][1] * v[1] + M[0][2] * v[2],
                M[1][0] * v[0] + M[1][1] * v[1] + M[1][2] * v[2],
                M[2][0] * v[0] + M[2][1] * v[1] + M[2][2] * v[2]
            };
        }

        // Eigenvalues (lamMax >= lamMin) and eigenvector angle theta (from +E axis, CCW)
        private static (double lamMax, double lamMin, double thetaRad) EigenSym2x2(double a, double b, double c)
        {
            double trace = a + c;
            double diff = a - c;
            double disc = Math.Sqrt(diff * diff + 4.0 * b * b);
            double lam1 = 0.5 * (trace + disc);
            double lam2 = 0.5 * (trace - disc);
            // Orientation of eigenvector associated with lam1
            double theta = 0.5 * Math.Atan2(2.0 * b, diff);
            return (lam1, lam2, theta);
        }

        private static double Deg2Rad(double d) => Math.PI * d / 180.0;
        private static double Rad2Deg(double r) => 180.0 * r / Math.PI;

        private static double Norm360(double d)
        {
            d %= 360.0;
            if (d < 0) d += 360.0;
            return d;
        }
    }
}
