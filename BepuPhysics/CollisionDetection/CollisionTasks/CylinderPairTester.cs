﻿using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection.SweepTasks;
using BepuUtilities;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BepuPhysics.CollisionDetection.CollisionTasks
{
    public struct CylinderPairTester : IPairTester<CylinderWide, CylinderWide, Convex4ContactManifoldWide>
    {
        public int BatchSize => 32;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetDepthContributionA(in CylinderWide a, in Vector<float> dotNAY, out Vector<float> contribution)
        {
            contribution = a.HalfLength * Vector.Abs(dotNAY) + a.Radius * Vector.SquareRoot(Vector.Max(Vector<float>.Zero, Vector<float>.One - dotNAY * dotNAY));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetDepthContributionB(in CylinderWide b, in Vector3Wide normal, out Vector<float> contribution)
        {
            contribution = b.HalfLength * Vector.Abs(normal.Y) + b.Radius * Vector.SquareRoot(Vector.Max(Vector<float>.Zero, Vector<float>.One - normal.Y * normal.Y));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetDepth(in Vector3Wide localAxisYA, in Vector3Wide localOffsetB, in Vector3Wide localNormal, in CylinderWide a, in CylinderWide b, out Vector<float> depth)
        {
            Vector3Wide.Dot(localAxisYA, localNormal, out var dotNAY);
            Vector3Wide.Dot(localOffsetB, localNormal, out var dotOffsetN);
            GetDepthContributionA(a, dotNAY, out var contributionA);
            GetDepthContributionB(b, localNormal, out var contributionB);
            depth = contributionA + contributionB - Vector.Abs(dotOffsetN);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProjectOntoCapA(in Vector<float> capCenterBY, in Vector3Wide capCenterA, in Matrix3x3Wide rA, in Vector<float> inverseNDotAY, in Vector3Wide localNormal, in Vector2Wide point, out Vector2Wide projected)
        {
            Vector3Wide point3D;
            point3D.X = point.X;
            point3D.Y = capCenterBY;
            point3D.Z = point.Y;
            ProjectOntoCapA(capCenterA, rA, inverseNDotAY, localNormal, point3D, out projected);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProjectOntoCapA(in Vector3Wide capCenterA, in Matrix3x3Wide rA, in Vector<float> inverseNDotAY, in Vector3Wide localNormal, in Vector3Wide point, out Vector2Wide projected)
        {
            Vector3Wide.Subtract(capCenterA, point, out var pointToCapCenterA);
            Vector3Wide.Dot(pointToCapCenterA, rA.Y, out var tDistance);
            var tBOnA = tDistance * inverseNDotAY;
            Vector3Wide.Scale(localNormal, tBOnA, out var projectionOffsetB);
            Vector3Wide.Add(point, projectionOffsetB, out var projectedPoint);
            Vector3Wide.Subtract(projectedPoint, capCenterA, out var capCenterAToProjectedPoint);
            Vector3Wide.Dot(capCenterAToProjectedPoint, rA.X, out projected.X);
            Vector3Wide.Dot(capCenterAToProjectedPoint, rA.Z, out projected.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ProjectOntoCapB(in Vector<float> capCenterBY, in Vector<float> inverseLocalNormalY, in Vector3Wide localNormal, in Vector3Wide point, out Vector2Wide projected)
        {
            var tAOnB = (point.Y - capCenterBY) * inverseLocalNormalY;
            projected.X = point.X - localNormal.X * tAOnB;
            projected.Y = point.Z - localNormal.Z * tAOnB;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void IntersectLineCircle(in Vector2Wide linePosition, in Vector2Wide lineDirection, in Vector<float> radius, out Vector<float> tMin, out Vector<float> tMax)
        {
            //||linePosition + lineDirection * t|| = radius
            //dot(linePosition + lineDirection * t, linePosition + lineDirection * t) = radius * radius
            //dot(linePosition, linePosition) - radius * radius + t * 2 * dot(linePosition, lineDirection) + t^2 * dot(lineDirection, lineDirection) = 0
            Vector2Wide.Dot(linePosition, linePosition, out var coefficientC);
            coefficientC -= radius * radius;
            Vector2Wide.Dot(linePosition, lineDirection, out var coefficientB);
            Vector2Wide.Dot(lineDirection, lineDirection, out var coefficientA);
            var inverseA = Vector<float>.One / coefficientA;
            var tOffset = Vector.SquareRoot(Vector.Max(Vector<float>.Zero, coefficientB * coefficientB - coefficientA * coefficientC)) * inverseA;
            var tBase = -coefficientB * inverseA;
            tMin = tBase - tOffset;
            tMax = tBase + tOffset;
            //If the projected length is zero, just treat both points as being in the same location (at tNegative).
            var useFallback = Vector.LessThan(Vector.Abs(coefficientA), new Vector<float>(1e-12f));
            tMin = Vector.ConditionalSelect(useFallback, Vector<float>.Zero, tMin);
            tMax = Vector.ConditionalSelect(useFallback, Vector<float>.Zero, tMax);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void FromCapBTo3D(in Vector2Wide contact, in Vector<float> capCenterBY, out Vector3Wide localContact3D)
        {
            localContact3D.X = contact.X;
            localContact3D.Y = capCenterBY;
            localContact3D.Z = contact.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void TransformContact(
            in Vector3Wide contact, in Vector3Wide localFeaturePositionA, in Vector3Wide localFeatureNormalA, Vector<float> inverseFeatureNormalADotLocalNormal,
            in Vector3Wide localOffsetB, in Matrix3x3Wide orientationB, Vector<float> negativeSpeculativeMargin,
            out Vector3Wide aToContact, out Vector<float> depth, ref Vector<int> contactExists)
        {
            //While we have computed a global depth, each contact has its own depth.
            //Project the contact on B along the contact normal to the 'face' of A.
            //The full computation is: 
            //t = dot(contact - featurePositionA, featureNormalA) / dot(featureNormalA, localNormal)
            //depth = dot(t * localNormal, localNormal) = t
            Vector3Wide.Subtract(contact, localFeaturePositionA, out var featureOffset);
            Vector3Wide.Dot(featureOffset, localFeatureNormalA, out var tDistance);
            depth = tDistance * inverseFeatureNormalADotLocalNormal;
            Vector3Wide.Add(contact, localOffsetB, out var localAToContact);
            Matrix3x3Wide.TransformWithoutOverlap(localAToContact, orientationB, out aToContact);
            contactExists = Vector.BitwiseAnd(contactExists, Vector.GreaterThanOrEqual(depth, negativeSpeculativeMargin));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Test(
            ref CylinderWide a, ref CylinderWide b, ref Vector<float> speculativeMargin,
            ref Vector3Wide offsetB, ref QuaternionWide orientationA, ref QuaternionWide orientationB, int pairCount,
            out Convex4ContactManifoldWide manifold)
        {
            Matrix3x3Wide.CreateFromQuaternion(orientationA, out var worldRA);
            Matrix3x3Wide.CreateFromQuaternion(orientationB, out var worldRB);
            //Work in b's local space.
            Matrix3x3Wide.MultiplyByTransposeWithoutOverlap(worldRA, worldRB, out var rA);
            Matrix3x3Wide.TransformByTransposedWithoutOverlap(offsetB, worldRB, out var localOffsetB);
            Vector3Wide.Negate(localOffsetB, out var localOffsetA);

            //Cylinder-sphere was easy, cylinder-capsule was a bit complicated, and things go downhill from there.
            //The potential normal generators are:
            //capNormalA: trivial
            //capNormalB: trivial
            //sideA vs sideB: capNormalA x capNormalB
            //capEdgeA vs sideB: minimum depth of circle and line, slightly more complex than the distance based implementation in capsule-cylinder because cylinders don't have a spherical margin
            //sideA vs capEdgeB: minimum depth of circle and line again
            //capEdgeA vs capEdgeB: uh oh

            //For reference, the distance between a line and circle (capEdge vs side) involves finding the minimal root of a fourth degree polynomial. 
            //The distance between two circles involves finding the minimal root of an eighth degree polynomial.
            //(We aren't computing *distance* here, but rather minimal depth, but computing minimal depth is, at best, no easier than distance.)

            //We can't actually use the same implementation as capsule-cylinder because it assumed nonnegative distance. In the intersecting case, it uses an approximation-
            //that's fine for a capsule which has a large margin, but it can't work for two cylinders.
            //Cylinder-cylinder has to handle the intersecting case accurately, which means you're stuck (conceptually) enumerating 4 or 8 roots per feature pair.
            //There are some pretty fast solvers around, but it's hard to avoid quite a few square roots given the shapes involved.
            //Plus, it would require a lot of special case mathing.

            //Given that we are going to encounter similar difficulties on cylinder-box, cylinder-triangle, and cylinder-hull, can we use some kind of trick to save time and effort?
            //The common approach would be minkowski space methods like GJK and MPR. v1 used them heavily, but I moved away from them for v2 due to numerical concerns.
            //But many of those numerical concerns had to do with contact generation, not finding normals; we can still use custom analytic contact generators with GJK and friends.

            //A couple of notes on convexity:
            //Separated convex objects have a global minimum (negative) depth; the distance function between convex objects is itself convex. GJK takes advantage of this.
            //Intersecting convex objects, in contrast, may have multiple local minima for depth. MPR doesn't find a global minimum depth- in fact, it might not even be a local minimum.
            //You can still sometimes get away with an suboptimal choice of depth, though. The important thing is that colliding objects are always found to be colliding, and separated objects are always found to be separated.

            //GJK and MPR are not the only possible algorithms in this space. For example, consider gradient descent. Starting from an initial guess normal, you can incrementally walk toward a local minimum 
            //through numerical (or in some cases even analytic) computation of the gradient. Gradient descent isn't always the quickest at converging, but if we have reasonably good starting points it can work.

            //Further, at least at a conceptual level, pure gradient descent is just as powerful as GJK or MPR for the purpose of finding a good normal. Enumerating the possible cases:
            //1) Intersecting; GD finds intersection: this will find, at worst, a local minimum informed by any initial queries.
            //2) Separated; GD finds separation: the separating case is convex, so GD can find the global optimum (ignoring convergence speed)
            //3) Intersecting; GD finds separation: impossible by the separating axis theorem
            //4) Separated; GD finds intersection: impossible with sufficient time- again, due to separation convexity, GD will (eventually) converge to the global separated optimum

            //So we have some options.

            //First, we'll try a few easy known normal candidates.
            //1) Offset from B to A
            Vector3Wide.Length(localOffsetA, out var length);
            Vector3Wide.Scale(localOffsetA, Vector<float>.One / length, out var localNormal);
            GetDepth(rA.Y, localOffsetB, localNormal, a, b, out var depth);
            depth = Vector.ConditionalSelect(Vector.LessThan(length, new Vector<float>(1e-7f)), new Vector<float>(float.MaxValue), depth);

            //2) Cap normal A
            GetDepthContributionB(b, rA.Y, out var capNormalAContributionB);
            Vector3Wide.Dot(localOffsetA, rA.Y, out var capNormalAOffsetDot);
            var capNormalADepth = a.HalfLength + capNormalAContributionB - Vector.Abs(capNormalAOffsetDot);
            var useCapNormalA = Vector.LessThan(capNormalADepth, depth);
            depth = Vector.ConditionalSelect(useCapNormalA, capNormalADepth, depth);
            Vector3Wide.ConditionalSelect(useCapNormalA, rA.Y, localNormal, out localNormal);

            //3) Cap normal B
            GetDepthContributionA(a, rA.Y.Y, out var capNormalBContributionA);
            var capNormalBDepth = b.HalfLength + capNormalBContributionA - Vector.Abs(localOffsetA.Y);
            var useCapNormalB = Vector.LessThan(capNormalBDepth, depth);
            depth = Vector.ConditionalSelect(useCapNormalB, capNormalBDepth, depth);
            localNormal.X = Vector.ConditionalSelect(useCapNormalB, Vector<float>.Zero, localNormal.X);
            localNormal.Y = Vector.ConditionalSelect(useCapNormalB, Vector<float>.One, localNormal.Y);
            localNormal.Z = Vector.ConditionalSelect(useCapNormalB, Vector<float>.Zero, localNormal.Z);

            //4) Axis A x axis B.
            var axisCrossLengthSquared = rA.Y.X * rA.Y.X + rA.Y.Z * rA.Y.Z;
            var axisCrossFallbackLengthSquared = localOffsetA.X * localOffsetA.X + localOffsetA.Z * localOffsetA.Z;
            //If the axes are parallel, just use the horizontal direction pointing from B to A.
            var useAxisCrossFallback = Vector.LessThan(axisCrossLengthSquared, new Vector<float>(1e-10f));
            var skipAxisCross = Vector.BitwiseAnd(useAxisCrossFallback, Vector.LessThan(axisCrossFallbackLengthSquared, new Vector<float>(1e-10f)));
            var inverseAxisCrossLength = Vector<float>.One / Vector.SquareRoot(Vector.ConditionalSelect(useAxisCrossFallback, axisCrossFallbackLengthSquared, axisCrossLengthSquared));
            Vector3Wide axisAxAxisB;
            axisAxAxisB.X = Vector.ConditionalSelect(useAxisCrossFallback, localOffsetA.X, -rA.Y.Z) * inverseAxisCrossLength;
            axisAxAxisB.Y = Vector<float>.Zero;
            axisAxAxisB.Z = Vector.ConditionalSelect(useAxisCrossFallback, localOffsetA.Z, rA.Y.X) * inverseAxisCrossLength;
            //By virtue of being perpendicular to both cylinder axes, there can be no contribution from the half lengths.
            Vector3Wide.Dot(axisAxAxisB, localOffsetA, out var axisCrossOffsetDot);
            var axisCrossDepth = a.Radius + b.Radius - Vector.Abs(axisCrossOffsetDot);
            var useAxisCross = Vector.AndNot(Vector.LessThan(axisCrossDepth, depth), skipAxisCross);
            depth = Vector.ConditionalSelect(useAxisCross, axisCrossDepth, depth);
            Vector3Wide.ConditionalSelect(useAxisCross, axisAxAxisB, localNormal, out localNormal);

            //Calibrate the normal to point from B to A.
            Vector3Wide.Dot(localNormal, localOffsetA, out var normalDotLocalOffsetB);
            Vector3Wide.ConditionallyNegate(Vector.LessThan(normalDotLocalOffsetB, Vector<float>.Zero), ref localNormal);

            //We now have a decent estimate for the local normal. Refine it to a local minimum.
            CylinderSupportFinder supportFinder = default;
            ManifoldCandidateHelper.CreateInactiveMask(pairCount, out var inactiveLanes);
            GradientDescent<Cylinder, CylinderWide, CylinderSupportFinder, Cylinder, CylinderWide, CylinderSupportFinder>.Refine(
                b, a, localOffsetA, rA, ref supportFinder, ref supportFinder, localNormal, -speculativeMargin, new Vector<float>(1e-4f), 25, inactiveLanes,
                out localNormal, out var depthBelowThreshold);
            inactiveLanes = Vector.BitwiseOr(depthBelowThreshold, inactiveLanes);

            if (Vector.LessThanAll(inactiveLanes, Vector<int>.Zero))
            {
                //All lanes are either inactive or were found to have a depth lower than the speculative margin, so we can just quit early.
                manifold = default;
                return;
            }

            //We generate contacts according to the dominant features along the collision normal.
            //The possible pairs are:
            //Cap A-Cap B
            //Cap A-Side B
            //Side A-Cap B
            //Side A-Side B
            Vector3Wide.Dot(rA.Y, localNormal, out var nDotAY);
            var inverseNDotAY = Vector<float>.One / nDotAY;
            var inverseLocalNormalY = Vector<float>.One / localNormal.Y;
            var capThreshold = new Vector<float>(0.70710678118f);
            var useCapA = Vector.GreaterThan(Vector.Abs(nDotAY), capThreshold);
            var useCapB = Vector.GreaterThan(Vector.Abs(localNormal.Y), capThreshold);
            Vector3Wide contact0, contact1, contact2, contact3;
            manifold.Contact0Exists = default;
            manifold.Contact1Exists = default;
            manifold.Contact2Exists = default;
            manifold.Contact3Exists = default;
            var useCapCap = Vector.AndNot(Vector.BitwiseAnd(useCapA, useCapB), inactiveLanes);


            //The extreme points along the contact normal are shared between multiple contact generator paths, so we just do them up front.
            Vector3Wide.Scale(rA.Y, Vector.ConditionalSelect(Vector.GreaterThan(nDotAY, Vector<float>.Zero), -a.HalfLength, a.HalfLength), out var capCenterA);
            Vector3Wide.Add(capCenterA, localOffsetA, out capCenterA);

            Vector3Wide.Dot(rA.X, localNormal, out var ax);
            Vector3Wide.Dot(rA.Z, localNormal, out var az);
            var horizontalNormalLengthA = Vector.SquareRoot(ax * ax + az * az);
            var inverseHorizontalNormalLengthA = Vector<float>.One / horizontalNormalLengthA;
            //No division by zero guard; we only use cap-cap for a lane if the local normal is well beyond perpendicular with the local cap normal.
            var normalizeScaleA = -a.Radius * inverseHorizontalNormalLengthA;
            var horizontalNormalLengthValidA = Vector.GreaterThan(horizontalNormalLengthA, new Vector<float>(1e-7f));
            Vector3Wide.Scale(rA.X, Vector.ConditionalSelect(horizontalNormalLengthValidA, ax * normalizeScaleA, Vector<float>.Zero), out var extremeAX);
            Vector3Wide.Scale(rA.Z, Vector.ConditionalSelect(horizontalNormalLengthValidA, az * normalizeScaleA, Vector<float>.Zero), out var extremeAZ);
            Vector3Wide.Add(extremeAX, extremeAZ, out var extremeAOffset);
            Vector3Wide.Add(extremeAOffset, capCenterA, out var extremeA);

            var capCenterBY = Vector.ConditionalSelect(Vector.LessThan(localNormal.Y, Vector<float>.Zero), -b.HalfLength, b.HalfLength);
            var horizontalNormalLengthSquaredB = localNormal.X * localNormal.X + localNormal.Z * localNormal.Z;
            var inverseHorizontalNormalLengthSquaredB = Vector<float>.One / horizontalNormalLengthSquaredB;
            var normalizeScaleB = b.Radius * Vector.SquareRoot(inverseHorizontalNormalLengthSquaredB);
            var horizontalNormalLengthValidB = Vector.GreaterThan(horizontalNormalLengthSquaredB, new Vector<float>(1e-10f));
            Vector2Wide extremeB;
            extremeB.X = Vector.ConditionalSelect(horizontalNormalLengthValidB, localNormal.X * normalizeScaleB, Vector<float>.Zero);
            extremeB.Y = Vector.ConditionalSelect(horizontalNormalLengthValidB, localNormal.Z * normalizeScaleB, Vector<float>.Zero);

            var useNegative = Vector.GreaterThan(nDotAY, Vector<float>.Zero);
            Vector3Wide capFeatureNormalA;
            capFeatureNormalA.X = Vector.ConditionalSelect(useNegative, -rA.Y.X, rA.Y.X);
            capFeatureNormalA.Y = Vector.ConditionalSelect(useNegative, -rA.Y.Y, rA.Y.Y);
            capFeatureNormalA.Z = Vector.ConditionalSelect(useNegative, -rA.Y.Z, rA.Y.Z);
            //We'll just assume every lane is cap-cap to start with; initialize the feature normal accordingly.
            var featureNormalA = capFeatureNormalA;
            var featurePositionA = capCenterA;

            if (Vector.LessThanAny(useCapCap, Vector<int>.Zero))
            {
                //At least one active lane needs cap-cap contacts.
                //While the obvious approach is to intersect the cylinder caps, doing so on the contact plane involves intersecting two ellipses.
                //That involves solving a fourth degree polynomial. Doable, but not super duper cheap. Further, when the caps are not parallel, 
                //intersecting the caps doesn't necessarily produce 'better' contacts. It's just a heuristic.

                //Instead, we'll start with extreme points, use them to draw a line, test that line against the caps, 
                //and then use a perpendicular line at the midpoint of the first line's intersection interval.

                var parallelThreshold = new Vector<float>(0.9999f);
                var aCapNotParallel = Vector.LessThan(Vector.Abs(nDotAY), parallelThreshold);
                var bCapNotParallel = Vector.LessThan(Vector.Abs(localNormal.Y), parallelThreshold);
                //The first contact should be chosen from the deepest points. This comes from one of three cases:
                //1) capNormalA is NOT parallel with localNormal; use the extreme point on A along -localNormal.
                Vector3Wide.Subtract(capCenterA, extremeAOffset, out var negativeExtremeA);

                //Project extreme A down onto capB.
                //No division by zero guard; we only use cap-cap for a lane if the local normal is well beyond perpendicular with the local cap normal.
                ProjectOntoCapB(capCenterBY, inverseLocalNormalY, localNormal, extremeA, out var capContact0);
                ProjectOntoCapB(capCenterBY, inverseLocalNormalY, localNormal, negativeExtremeA, out var contact1LineEndpoint);

                //2) capNormalB is NOT parallel with localNormal and capNormalA IS parallel with localNormal; use the extreme point on B along localNormal.
                var useExtremeB = Vector.AndNot(bCapNotParallel, aCapNotParallel);
                Vector2Wide.ConditionalSelect(useExtremeB, extremeB, capContact0, out capContact0);
                contact1LineEndpoint.X = Vector.ConditionalSelect(useExtremeB, -extremeB.X, contact1LineEndpoint.X);
                contact1LineEndpoint.Y = Vector.ConditionalSelect(useExtremeB, -extremeB.Y, contact1LineEndpoint.Y);

                //3) Both capNormalA and capNormalB are parallel with localNormal; use min(b.Radius, ||ba|| + a.Radius) * ba / ||ba||.
                ProjectOntoCapB(capCenterBY, inverseLocalNormalY, localNormal, capCenterA, out var capCenterAOnB);
                Vector2Wide.Length(capCenterAOnB, out var horizontalOffsetLength);
                var inverseHorizontalOffsetLength = Vector<float>.One / horizontalOffsetLength;
                Vector2Wide.Scale(capCenterAOnB, Vector.Min(b.Radius, horizontalOffsetLength + a.Radius) * inverseHorizontalOffsetLength, out var bothParallelContact0);
                //If both caps were parallel, then contact1 is just min(b.Radius, max(-b.Radius, ||ba|| - a.Radius)) * ba / ||ba||.
                Vector2Wide.Scale(capCenterAOnB, Vector.Min(b.Radius, Vector.Max(-b.Radius, horizontalOffsetLength - a.Radius)) * inverseHorizontalOffsetLength, out var bothParallelContact1);
                var bothParallel = Vector.AndNot(Vector.OnesComplement(aCapNotParallel), bCapNotParallel);
                var useBothParallelFallback = Vector.LessThan(horizontalOffsetLength, new Vector<float>(1e-7f));
                capContact0.X = Vector.ConditionalSelect(bothParallel, Vector.ConditionalSelect(useBothParallelFallback, b.Radius, bothParallelContact0.X), capContact0.X);
                capContact0.Y = Vector.ConditionalSelect(bothParallel, Vector.ConditionalSelect(useBothParallelFallback, Vector<float>.Zero, bothParallelContact0.Y), capContact0.Y);
                contact1LineEndpoint.X = Vector.ConditionalSelect(bothParallel, Vector.ConditionalSelect(useBothParallelFallback, -b.Radius, bothParallelContact1.X), contact1LineEndpoint.X);
                contact1LineEndpoint.Y = Vector.ConditionalSelect(bothParallel, Vector.ConditionalSelect(useBothParallelFallback, Vector<float>.Zero, bothParallelContact1.Y), contact1LineEndpoint.Y);

                //The above created a line stating at contact0 and pointing to the other side of the cap that contact0 was generated from.
                //That is, if the extreme point from A was used, then the line direction is localNormal projected on capA.
                Vector3Wide capCenterBToCapCenterA;
                capCenterBToCapCenterA.X = -capCenterA.X;
                capCenterBToCapCenterA.Y = capCenterBY - capCenterA.Y;
                capCenterBToCapCenterA.Z = -capCenterA.Z;
                ProjectOntoCapA(capCenterBY, capCenterA, rA, inverseNDotAY, localNormal, capContact0, out var lineStartOnA);
                ProjectOntoCapA(capCenterBY, capCenterA, rA, inverseNDotAY, localNormal, contact1LineEndpoint, out var lineEndOnA);
                Vector2Wide.Subtract(lineEndOnA, lineStartOnA, out var lineDirectionOnA);
                Vector2Wide.Subtract(contact1LineEndpoint, capContact0, out var contact1LineDirectionOnB);
                IntersectLineCircle(lineStartOnA, lineDirectionOnA, a.Radius, out var contact1TMinA, out var contact1TMaxA);
                IntersectLineCircle(capContact0, contact1LineDirectionOnB, b.Radius, out var contact1TMinB, out var contact1TMaxB);
                var firstLineTMax = Vector.Min(contact1TMaxA, contact1TMaxB);
                Vector2Wide.Scale(contact1LineDirectionOnB, firstLineTMax, out var capContact1);
                Vector2Wide.Add(capContact0, capContact1, out capContact1);

                //The line from contact0 to contact1 on capB provides the direction for the second line. Just use a perpendicular direction and start the ray at 
                //the midpoint between contact0 and contact1. Intersect the line against both caps.
                Vector2Wide secondLineDirectionOnB;
                secondLineDirectionOnB.X = contact1LineDirectionOnB.Y;
                secondLineDirectionOnB.Y = -contact1LineDirectionOnB.X;
                Vector2Wide secondLineStartOnB;
                secondLineStartOnB.X = (capContact0.X + capContact1.X) * 0.5f;
                secondLineStartOnB.Y = (capContact0.Y + capContact1.Y) * 0.5f;

                Vector2Wide.Add(secondLineStartOnB, secondLineDirectionOnB, out var secondLineEndOnB);
                ProjectOntoCapA(capCenterBY, capCenterA, rA, inverseNDotAY, localNormal, secondLineStartOnB, out var secondLineStartOnA);
                ProjectOntoCapA(capCenterBY, capCenterA, rA, inverseNDotAY, localNormal, secondLineEndOnB, out var secondLineEndOnA);
                Vector2Wide.Subtract(secondLineEndOnA, secondLineStartOnA, out var secondLineDirectionOnA);

                IntersectLineCircle(secondLineStartOnA, secondLineDirectionOnA, a.Radius, out var secondLineTMinA, out var secondLineTMaxA);
                IntersectLineCircle(secondLineStartOnB, secondLineDirectionOnB, b.Radius, out var secondLineTMinB, out var secondLineTMaxB);
                var secondLineTMin = Vector.Max(secondLineTMinA, secondLineTMinB);
                var secondLineTMax = Vector.Min(secondLineTMaxA, secondLineTMaxB);

                Vector2Wide.Scale(secondLineDirectionOnB, secondLineTMin, out var capContact2);
                Vector2Wide.Scale(secondLineDirectionOnB, secondLineTMax, out var capContact3);
                Vector2Wide.Add(secondLineStartOnB, capContact2, out capContact2);
                Vector2Wide.Add(secondLineStartOnB, capContact3, out capContact3);

                FromCapBTo3D(capContact0, capCenterBY, out contact0);
                FromCapBTo3D(capContact1, capCenterBY, out contact1);
                FromCapBTo3D(capContact2, capCenterBY, out contact2);
                FromCapBTo3D(capContact3, capCenterBY, out contact3);
                manifold.Contact0Exists = useCapCap;
                manifold.Contact1Exists = Vector.BitwiseAnd(useCapCap, Vector.GreaterThan(firstLineTMax, Vector<float>.Zero));
                //If 0 and 1 are in the same spot, there aren't going to be any useful additional contacts.
                manifold.Contact2Exists = manifold.Contact1Exists;
                manifold.Contact3Exists = Vector.BitwiseAnd(manifold.Contact1Exists, Vector.GreaterThan(secondLineTMax, secondLineTMin));
            }
            var useCapSide = Vector.AndNot(Vector.BitwiseOr(Vector.AndNot(useCapA, useCapB), Vector.AndNot(useCapB, useCapA)), inactiveLanes);
            //The side normal is used in both of the following contact generator cases.
            var xScale = ax * inverseHorizontalNormalLengthA;
            var zScale = az * inverseHorizontalNormalLengthA;
            Vector3Wide.Scale(rA.X, xScale, out var sideFeatureNormalAX);
            Vector3Wide.Scale(rA.Z, zScale, out var sideFeatureNormalAZ);
            Vector3Wide.Add(sideFeatureNormalAX, sideFeatureNormalAZ, out var sideFeatureNormalA);
            Vector3Wide sideCenterA, sideCenterB;
            sideCenterA.X = extremeAOffset.X + localOffsetA.X;
            sideCenterA.Y = extremeAOffset.Y + localOffsetA.Y;
            sideCenterA.Z = extremeAOffset.Z + localOffsetA.Z;
            sideCenterB.X = extremeB.X;
            sideCenterB.Y = Vector<float>.Zero;
            sideCenterB.Z = extremeB.Y;
            if (Vector.LessThanAny(useCapSide, Vector<int>.Zero))
            {
                //At least one lane needs cap-side contact generation.
                //This is relatively simple:
                //Pick a line on the side of the cylinder in the direction of the extreme point.
                //Pick the cap on the other cylinder in the direction of the extreme point.
                //Intersect the line with the cap in the cap's local space (by projecting the line along the contact normal into the cap's local space).

                //Compute both lines and choose which one to use.
                Vector3Wide.Add(sideCenterA, rA.Y, out var sideLineEndA);
                Vector3Wide sideLineEndB;
                sideLineEndB.X = sideCenterB.X;
                sideLineEndB.Y = Vector<float>.One;
                sideLineEndB.Z = sideCenterB.Z;
                ProjectOntoCapA(capCenterA, rA, inverseNDotAY, localNormal, sideCenterB, out var projectedLineStartBOnA);
                ProjectOntoCapB(capCenterBY, inverseLocalNormalY, localNormal, sideCenterA, out var projectedLineStartAOnB);
                ProjectOntoCapA(capCenterA, rA, inverseNDotAY, localNormal, sideLineEndB, out var projectedLineEndBOnA);
                ProjectOntoCapB(capCenterBY, inverseLocalNormalY, localNormal, sideLineEndA, out var projectedLineEndAOnB);

                Vector2Wide.ConditionalSelect(useCapA, projectedLineStartBOnA, projectedLineStartAOnB, out var projectedLineStart);
                Vector2Wide.ConditionalSelect(useCapA, projectedLineEndBOnA, projectedLineEndAOnB, out var projectedLineEnd);
                var radius = Vector.ConditionalSelect(useCapA, a.Radius, b.Radius);
                var sideHalfLength = Vector.ConditionalSelect(useCapA, b.HalfLength, a.HalfLength);
                Vector2Wide.Subtract(projectedLineEnd, projectedLineStart, out var projectedLineDirection);
                IntersectLineCircle(projectedLineStart, projectedLineDirection, radius, out var tMin, out var tMax);
                tMin = Vector.Max(-sideHalfLength, tMin);
                tMax = Vector.Min(sideHalfLength, tMax);

                //To be consistent with the other contact generation cases, we want contacts to be on cylinder B. So:
                //If the cap was on A, that means the side was B and we should scale the sideLineB to get our contacts.
                //If the cap was on B, that means the side was on A and we should use the *projected* sideLineA to get our contacts.
                Vector3Wide contact0ForCapA;
                contact0ForCapA.X = sideCenterB.X;
                contact0ForCapA.Y = tMin;
                contact0ForCapA.Z = sideCenterB.Z;
                Vector3Wide contact1ForCapA;
                contact1ForCapA.X = sideCenterB.X;
                contact1ForCapA.Y = tMax;
                contact1ForCapA.Z = sideCenterB.Z;
                //Note that we're using the post-conditionalselect projectedLineStart and direction here.
                //That's fine; we only use these results if that choice is actually correct anyway.
                Vector3Wide contact0ForCapB;
                contact0ForCapB.X = projectedLineStart.X + tMin * projectedLineDirection.X;
                contact0ForCapB.Y = capCenterBY;
                contact0ForCapB.Z = projectedLineStart.Y + tMin * projectedLineDirection.Y;
                Vector3Wide contact1ForCapB;
                contact1ForCapB.X = projectedLineStart.X + tMax * projectedLineDirection.X;
                contact1ForCapB.Y = capCenterBY;
                contact1ForCapB.Z = projectedLineStart.Y + tMax * projectedLineDirection.Y;
                Vector3Wide.ConditionalSelect(useCapA, contact0ForCapA, contact0ForCapB, out var capSideContact0);
                Vector3Wide.ConditionalSelect(useCapA, contact1ForCapA, contact1ForCapB, out var capSideContact1);
                Vector3Wide.ConditionalSelect(useCapSide, capSideContact0, contact0, out contact0);
                Vector3Wide.ConditionalSelect(useCapSide, capSideContact1, contact1, out contact1);
                manifold.Contact0Exists = Vector.ConditionalSelect(useCapSide, new Vector<int>(-1), manifold.Contact0Exists);
                manifold.Contact1Exists = Vector.ConditionalSelect(useCapSide, Vector.GreaterThan(tMax, tMin), manifold.Contact1Exists);

                Vector3Wide.ConditionalSelect(useCapA, capFeatureNormalA, sideFeatureNormalA, out var capSideFeatureNormalA);
                Vector3Wide.ConditionalSelect(useCapSide, capSideFeatureNormalA, featureNormalA, out featureNormalA);

                Vector3Wide.ConditionalSelect(useCapA, capCenterA, sideCenterA, out var capSideFeaturePositionA);
                Vector3Wide.ConditionalSelect(useCapSide, capSideFeaturePositionA, featurePositionA, out featurePositionA);
            }
            var useSideSide = Vector.AndNot(Vector.AndNot(Vector.OnesComplement(useCapA), useCapB), inactiveLanes);
            if (Vector.LessThanAny(useSideSide, Vector<int>.Zero))
            {
                //At least one lane needs side-side contacts.
                //Project A's line onto B's line along the contact normal.
                //lineEndpointAProjectedOntoBPlaneAlongNormal = lineEndpointA - localNormal * dot(lineEndpointA - sideCenterB, sideNormalB) / dot(localNormal, sideNormalB)
                //tAOnB = dot(lineDirectionB, lineEndpointAProjectedOntoBPlaneAlongNormal)
                //tAOnB = dot(lineDirectionB, lineEndpointA - localNormal * dot(lineEndpointA - sideCenterB, sideNormalB) / dot(localNormal, sideNormalB))
                //tAOnB = lineEndpointA.Y - localNormal.Y * dot(lineEndpointA - sideCenterB, sideNormalB) / dot(localNormal, sideNormalB))
                //No division guard; any lane that uses this path will have a local normal that has some component along the horizontal direction.
                //var inverseLocalNormalDotSideNormalB = Vector<float>.One / (localNormal.X * extremeB.X + localNormal.Z * extremeB.Y);
                //Vector3Wide.Scale(rA.Y, a.HalfLength, out var lineEndpointOffsetA);
                //Vector3Wide.Subtract(sideCenterA, sideCenterB, out var sideCenterBToSideCenterA);
                //Vector3Wide.Subtract(sideCenterBToSideCenterA, lineEndpointOffsetA, out var minEndpointA);
                //Vector3Wide.Add(sideCenterBToSideCenterA, lineEndpointOffsetA, out var maxEndpointA);
                //var tMin = (extremeB.X * minEndpointA.X + extremeB.Y * minEndpointA.Z) * inverseLocalNormalDotSideNormalB;
                //var tMax = (extremeB.X * maxEndpointA.X + extremeB.Y * maxEndpointA.Z) * inverseLocalNormalDotSideNormalB;
                //Vector3Wide.Scale(localNormal, tMin, out var minOffset);
                //Vector3Wide.Scale(localNormal, tMax, out var maxOffset);
                //Vector3Wide.Subtract(minEndpointA, minOffset, out var projectedMinA);
                //Vector3Wide.Subtract(maxEndpointA, maxOffset, out var projectedMaxA);
                //Vector3Wide.Subtract(projectedMaxA, projectedMinA, out var minToMax);
                ////This could be simplified a bit. We're just doing the naive direct computation.
                //Vector3Wide.Length(minToMax, out var projectedLineDirectionLength);
                //Vector3Wide.Scale(minToMax, Vector<float>.One / projectedLineDirectionLength, out var projectedLineDirection);
                //Vector3Wide.Add(projectedMinA, projectedMaxA, out var projectedLineCenter);

                //Vector3Wide.Scale(projectedLineCenter, new Vector<float>(0.5f), out var projectedLineAToLineB);
                //projectedLineAToLineB.X = extremeB.X - projectedLineAToLineB.X;
                //projectedLineAToLineB.Y = -projectedLineAToLineB.Y;
                //projectedLineAToLineB.Z = extremeB.Y - projectedLineAToLineB.Z;
                //CapsuleCylinderTester.GetContactIntervalBetweenSegments(projectedLineDirectionLength * 0.5f, b.HalfLength, projectedLineDirection, localNormal, inverseHorizontalNormalLengthSquaredB, projectedLineAToLineB, out var contactTMin, out var contactTMax);

                //Vector3Wide.Scale(projectedLineCenter, new Vector<float>(0.5f), out var projectedLineAToLineB);
                //projectedLineAToLineB.X = -projectedLineAToLineB.X;
                //projectedLineAToLineB.Y = -projectedLineAToLineB.Y;
                //projectedLineAToLineB.Z = -projectedLineAToLineB.Z;
                //CapsuleCylinderTester.GetContactIntervalBetweenSegments(projectedLineDirectionLength * 0.5f, b.HalfLength, projectedLineDirection, localNormal, inverseHorizontalNormalLengthSquaredB, projectedLineAToLineB, out var contactTMin, out var contactTMax);

                //var contactTMin = Vector.Max(-b.HalfLength, Vector.Min(projectedMinA.Y, projectedMaxA.Y));
                //var contactTMax = Vector.Min(b.HalfLength, Vector.Max(projectedMinA.Y, projectedMaxA.Y));

                //Vector3Wide.Subtract(sideCenterB, sideCenterA, out var sideCenterAToSideCenterB);
                //Note that we test the line on the side of A against the line *in the center of* B. We then use its interval on the side of B.
                //(Using the side of B to detect the interval can result in issues in medium-depth penetration where the deepest point is ignored in favor of the closest point between the side lines.)
                Vector3Wide.Negate(sideCenterA, out var sideCenterAToLineB);
                CapsuleCylinderTester.GetContactIntervalBetweenSegments(a.HalfLength, b.HalfLength, rA.Y, localNormal, inverseHorizontalNormalLengthSquaredB, sideCenterAToLineB, out var contactTMin, out var contactTMax);


                contact0.X = Vector.ConditionalSelect(useSideSide, extremeB.X, contact0.X);
                contact0.Y = contactTMin;
                contact0.Z = Vector.ConditionalSelect(useSideSide, extremeB.Y, contact0.Z);
                contact1.X = Vector.ConditionalSelect(useSideSide, extremeB.X, contact1.X);
                contact1.Y = contactTMax;
                contact1.Z = Vector.ConditionalSelect(useSideSide, extremeB.Y, contact1.Z);
                manifold.Contact0Exists = Vector.ConditionalSelect(useSideSide, new Vector<int>(-1), manifold.Contact0Exists);
                manifold.Contact1Exists = Vector.ConditionalSelect(useSideSide, Vector.GreaterThan(contactTMax, contactTMin), manifold.Contact0Exists);
                Vector3Wide.ConditionalSelect(useSideSide, sideFeatureNormalA, featureNormalA, out featureNormalA);
                Vector3Wide.ConditionalSelect(useSideSide, sideCenterA, featurePositionA, out featurePositionA);
            }

            Vector3Wide.Dot(featureNormalA, localNormal, out var featureNormalADotLocalNormal);
            //No division guard; the feature normal we picked is never more than 45 degrees away from the local normal.
            var inverseFeatureNormalADotLocalNormal = Vector<float>.One / featureNormalADotLocalNormal;
            var negativeSpeculativeMargin = -speculativeMargin;
            TransformContact(contact0, featurePositionA, featureNormalA, inverseFeatureNormalADotLocalNormal, localOffsetB, worldRB, negativeSpeculativeMargin, out manifold.OffsetA0, out manifold.Depth0, ref manifold.Contact0Exists);
            TransformContact(contact1, featurePositionA, featureNormalA, inverseFeatureNormalADotLocalNormal, localOffsetB, worldRB, negativeSpeculativeMargin, out manifold.OffsetA1, out manifold.Depth1, ref manifold.Contact1Exists);
            TransformContact(contact2, featurePositionA, featureNormalA, inverseFeatureNormalADotLocalNormal, localOffsetB, worldRB, negativeSpeculativeMargin, out manifold.OffsetA2, out manifold.Depth2, ref manifold.Contact2Exists);
            TransformContact(contact3, featurePositionA, featureNormalA, inverseFeatureNormalADotLocalNormal, localOffsetB, worldRB, negativeSpeculativeMargin, out manifold.OffsetA3, out manifold.Depth3, ref manifold.Contact3Exists);
            Matrix3x3Wide.TransformWithoutOverlap(localNormal, worldRB, out manifold.Normal);
            //Contact generators all obey a reasonably solid order, so we can use a trivial feature description.
            //Note that this will conflate different contact generator cases, but that's okay- we tend to keep the most important contact in the first slot, for example, and that is the contact that is most likely to persist across feature pair changes.
            manifold.FeatureId0 = Vector<int>.Zero;
            manifold.FeatureId1 = Vector<int>.One;
            manifold.FeatureId2 = new Vector<int>(2);
            manifold.FeatureId3 = new Vector<int>(3);


        }

        public void Test(ref CylinderWide a, ref CylinderWide b, ref Vector<float> speculativeMargin, ref Vector3Wide offsetB, ref QuaternionWide orientationB, int pairCount, out Convex4ContactManifoldWide manifold)
        {
            throw new NotImplementedException();
        }

        public void Test(ref CylinderWide a, ref CylinderWide b, ref Vector<float> speculativeMargin, ref Vector3Wide offsetB, int pairCount, out Convex4ContactManifoldWide manifold)
        {
            throw new NotImplementedException();
        }
    }
}