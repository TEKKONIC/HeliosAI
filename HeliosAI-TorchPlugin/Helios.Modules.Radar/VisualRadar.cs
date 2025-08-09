using System;
using NLog;
using VRageMath;
using VRage.ModAPI;

namespace HeliosAI.Radar
{
    public static class VisualRadar
    {
        private static readonly Logger Logger = LogManager.GetLogger("VisualRadar");
        private const int DefaultSegments = 64;
        private const float DefaultLineWidth = 0.05f;
        private const float DefaultTargetLineWidth = 0.1f;

        /// <summary>
        /// Draws a radar circle with optional target line
        /// </summary>
        /// <param name="center">Center position of the radar</param>
        /// <param name="radius">Radius of the radar circle</param>
        /// <param name="target">Optional target to draw line to</param>
        /// <param name="color">Color of the radar circle</param>
        /// <param name="targetColor">Color of the target line</param>
        public static void DrawRadar(Vector3D center, double radius, IMyEntity target = null, 
            Color? color = null, Color? targetColor = null)
        {
            if (radius <= 0)
            {
                Logger.Warn($"Invalid radar radius: {radius}");
                return;
            }

            try
            {
                var radarColor = color ?? Color.White;
                var lineColor = targetColor ?? Color.Red;

                DrawRadarCircle(center, radius, radarColor);

                if (target != null)
                {
                    DrawTargetLine(center, target, lineColor);
                }

                Logger.Debug($"Radar drawn at {center} with radius {radius}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to draw radar at {center} with radius {radius}");
            }
        }

        private static void DrawRadarCircle(Vector3D center, double radius, Color color)
        {
            try
            {
                var segments = DefaultSegments;
                var angleStep = 2 * Math.PI / segments;
                var colorVector = color.ToVector4();

                for (var i = 0; i < segments; i++)
                {
                    var angle1 = i * angleStep;
                    var angle2 = (i + 1) * angleStep;

                    var p1 = center + new Vector3D(Math.Cos(angle1), 0, Math.Sin(angle1)) * radius;
                    var p2 = center + new Vector3D(Math.Cos(angle2), 0, Math.Sin(angle2)) * radius;

                    // TODO: Uncomment when rendering is available
                    // MyTransparentGeometry.DrawLineBillboard(
                    //     MyStringId.GetOrCompute("WeaponLaser"), 
                    //     colorVector, 
                    //     p1, 
                    //     (Vector3)(p2 - p1), 
                    //     DefaultLineWidth
                    // );
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to draw radar circle");
            }
        }

        private static void DrawTargetLine(Vector3D center, IMyEntity target, Color color)
        {
            if (target == null)
                return;

            try
            {
                var targetPosition = target.GetPosition();
                var colorVector = color.ToVector4();

                // TODO: Uncomment when rendering is available
                // MyTransparentGeometry.DrawLineBillboard(
                //     MyStringId.GetOrCompute("WeaponLaser"), 
                //     colorVector, 
                //     center, 
                //     (Vector3)(targetPosition - center), 
                //     DefaultTargetLineWidth
                // );

                Logger.Debug($"Target line drawn from {center} to {targetPosition}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to draw target line to {target?.DisplayName}");
            }
        }

        public static void DrawRadarWithMultipleTargets(Vector3D center, double radius, 
            IMyEntity[] targets, Color? radarColor = null, Color? targetColor = null)
        {
            if (targets == null)
            {
                Logger.Warn("Attempted to draw radar with null targets array");
                return;
            }

            try
            {
                DrawRadarCircle(center, radius, radarColor ?? Color.White);

                var lineColor = targetColor ?? Color.Red;
                foreach (var target in targets)
                {
                    if (target != null)
                    {
                        DrawTargetLine(center, target, lineColor);
                    }
                }

                Logger.Debug($"Multi-target radar drawn with {targets.Length} targets");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to draw multi-target radar");
            }
        }

        public static void DrawRadarSweep(Vector3D center, double radius, double sweepAngle, 
            Vector3D direction, Color? color = null)
        {
            if (radius <= 0)
            {
                Logger.Warn($"Invalid sweep radius: {radius}");
                return;
            }

            try
            {
                var sweepColor = color ?? Color.Yellow;
                var colorVector = sweepColor.ToVector4();

                // Draw sweep line
                var sweepEnd = center + Vector3D.Normalize(direction) * radius;
                
                // TODO: Uncomment when rendering is available
                // MyTransparentGeometry.DrawLineBillboard(
                //     MyStringId.GetOrCompute("WeaponLaser"), 
                //     colorVector, 
                //     center, 
                //     (Vector3)(sweepEnd - center), 
                //     DefaultTargetLineWidth
                // );

                Logger.Debug($"Radar sweep drawn at angle {sweepAngle} degrees");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to draw radar sweep");
            }
        }

        public static bool IsTargetInRadarRange(Vector3D radarCenter, double radarRadius, IMyEntity target)
        {
            if (target == null)
                return false;

            try
            {
                var distance = Vector3D.Distance(radarCenter, target.GetPosition());
                return distance <= radarRadius;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to check if target {target?.DisplayName} is in radar range");
                return false;
            }
        }
    }
}