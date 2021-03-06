// ****************************************************************************
// Copyright (c) 2011, Daniel Murphy
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without modification,
// are permitted provided that the following conditions are met:
// * Redistributions of source code must retain the above copyright notice,
// this list of conditions and the following disclaimer.
// * Redistributions in binary form must reproduce the above copyright notice,
// this list of conditions and the following disclaimer in the documentation
// and/or other materials provided with the distribution.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
// IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT,
// INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
// NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
// PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
// WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
// POSSIBILITY OF SUCH DAMAGE.
// ****************************************************************************

using System;
using System.Diagnostics;
using Box2D.Collision;
using Box2D.Collision.Shapes;
using Box2D.Common;

namespace Box2D.Dynamics.Contacts
{

    /// <author>Daniel</author>
    public class ContactSolver
    {
        public const bool DEBUG_SOLVER = false;
        public const float k_errorTol = 1e-3f;

        /// <summary>
        /// For each solver, this is the initial number of constraints in the array, which expands as
        /// needed.
        /// </summary>
        public const int INITIAL_NUM_CONSTRAINTS = 256;

        /// <summary>
        /// Ensure a reasonable condition number. for the block solver
        /// </summary>
        public const float k_maxConditionNumber = 100.0f;

        public TimeStep m_step;
        public Position[] m_positions;
        public Velocity[] m_velocities;
        public ContactPositionConstraint[] m_positionConstraints;
        public ContactVelocityConstraint[] m_velocityConstraints;
        public Contact[] m_contacts;
        public int m_count;

        public ContactSolver()
        {
            m_positionConstraints = new ContactPositionConstraint[INITIAL_NUM_CONSTRAINTS];
            m_velocityConstraints = new ContactVelocityConstraint[INITIAL_NUM_CONSTRAINTS];
            for (int i = 0; i < INITIAL_NUM_CONSTRAINTS; i++)
            {
                m_positionConstraints[i] = new ContactPositionConstraint();
                m_velocityConstraints[i] = new ContactVelocityConstraint();
            }
        }

        // djm pooling
        private readonly Vec2 tangent = new Vec2();
        private readonly Vec2 temp1 = new Vec2();
        private readonly Vec2 temp2 = new Vec2();

        public void init(ContactSolverDef def)
        {
            //Console.WriteLine("Initializing contact solver");
            m_step = def.step;
            m_count = def.count;


            if (m_positionConstraints.Length < m_count)
            {
                ContactPositionConstraint[] old = m_positionConstraints;
                m_positionConstraints = new ContactPositionConstraint[MathUtils.max(old.Length * 2, m_count)];
                Array.Copy(old, 0, m_positionConstraints, 0, old.Length);
                for (int i = old.Length; i < m_positionConstraints.Length; i++)
                {
                    m_positionConstraints[i] = new ContactPositionConstraint();
                }
            }

            if (m_velocityConstraints.Length < m_count)
            {
                ContactVelocityConstraint[] old = m_velocityConstraints;
                m_velocityConstraints = new ContactVelocityConstraint[MathUtils.max(old.Length * 2, m_count)];
                Array.Copy(old, 0, m_velocityConstraints, 0, old.Length);
                for (int i = old.Length; i < m_velocityConstraints.Length; i++)
                {
                    m_velocityConstraints[i] = new ContactVelocityConstraint();
                }
            }

            m_positions = def.positions;
            m_velocities = def.velocities;
            m_contacts = def.contacts;

            for (int i = 0; i < m_count; ++i)
            {
                //Console.WriteLine("contacts: " + m_count);
                Contact contact = m_contacts[i];

                Fixture fixtureA = contact.m_fixtureA;
                Fixture fixtureB = contact.m_fixtureB;
                Shape shapeA = fixtureA.Shape;
                Shape shapeB = fixtureB.Shape;
                float radiusA = shapeA.m_radius;
                float radiusB = shapeB.m_radius;
                Body bodyA = fixtureA.Body;
                Body bodyB = fixtureB.Body;
                Manifold manifold = contact.Manifold;

                int pointCount = manifold.pointCount;
                Debug.Assert(pointCount > 0);

                ContactVelocityConstraint vc = m_velocityConstraints[i];
                vc.friction = contact.m_friction;
                vc.restitution = contact.m_restitution;
                vc.tangentSpeed = contact.m_tangentSpeed;
                vc.indexA = bodyA.m_islandIndex;
                vc.indexB = bodyB.m_islandIndex;
                vc.invMassA = bodyA.m_invMass;
                vc.invMassB = bodyB.m_invMass;
                vc.invIA = bodyA.m_invI;
                vc.invIB = bodyB.m_invI;
                vc.contactIndex = i;
                vc.pointCount = pointCount;
                vc.K.setZero();
                vc.normalMass.setZero();

                ContactPositionConstraint pc = m_positionConstraints[i];
                pc.indexA = bodyA.m_islandIndex;
                pc.indexB = bodyB.m_islandIndex;
                pc.invMassA = bodyA.m_invMass;
                pc.invMassB = bodyB.m_invMass;
                pc.localCenterA.set_Renamed(bodyA.m_sweep.localCenter);
                pc.localCenterB.set_Renamed(bodyB.m_sweep.localCenter);
                pc.invIA = bodyA.m_invI;
                pc.invIB = bodyB.m_invI;
                pc.localNormal.set_Renamed(manifold.localNormal);
                pc.localPoint.set_Renamed(manifold.localPoint);
                pc.pointCount = pointCount;
                pc.radiusA = radiusA;
                pc.radiusB = radiusB;
                pc.type = manifold.type;

                //Console.WriteLine("contact point count: " + pointCount);
                for (int j = 0; j < pointCount; j++)
                {
                    ManifoldPoint cp = manifold.points[j];
                    ContactVelocityConstraint.VelocityConstraintPoint vcp = vc.points[j];

                    if (m_step.warmStarting)
                    {
                        //Debug.Assert(cp.normalImpulse == 0);
                        //Console.WriteLine("contact normal impulse: " + cp.normalImpulse);
                        vcp.normalImpulse = m_step.dtRatio * cp.normalImpulse;
                        vcp.tangentImpulse = m_step.dtRatio * cp.tangentImpulse;
                    }
                    else
                    {
                        vcp.normalImpulse = 0;
                        vcp.tangentImpulse = 0;
                    }

                    vcp.rA.setZero();
                    vcp.rB.setZero();
                    vcp.normalMass = 0;
                    vcp.tangentMass = 0;
                    vcp.velocityBias = 0;

                    pc.localPoints[j].set_Renamed(cp.localPoint);
                }
            }
        }

        // djm pooling, and from above
        private readonly Vec2 P = new Vec2();
        private readonly Vec2 temp = new Vec2();

        public virtual void warmStart()
        {
            // Warm start.
            for (int i = 0; i < m_count; ++i)
            {
                ContactVelocityConstraint vc = m_velocityConstraints[i];

                int indexA = vc.indexA;
                int indexB = vc.indexB;
                float mA = vc.invMassA;
                float iA = vc.invIA;
                float mB = vc.invMassB;
                float iB = vc.invIB;
                int pointCount = vc.pointCount;


                Vec2 vA = m_velocities[indexA].v;
                float wA = m_velocities[indexA].w;
                Vec2 vB = m_velocities[indexB].v;
                float wB = m_velocities[indexB].w;

                Vec2 normal = vc.normal;
                Vec2.crossToOutUnsafe(normal, 1.0f, tangent);

                for (int j = 0; j < pointCount; ++j)
                {
                    ContactVelocityConstraint.VelocityConstraintPoint vcp = vc.points[j];
                    //Console.WriteLine("vcp normal impulse is " + vcp.normalImpulse);
                    temp.set_Renamed(normal).mulLocal(vcp.normalImpulse);
                    P.set_Renamed(tangent).mulLocal(vcp.tangentImpulse).addLocal(temp);
                    wA -= iA * Vec2.cross(vcp.rA, P);
                    vA.subLocal(temp.set_Renamed(P).mulLocal(mA));
                    //Debug.Assert(vA.x == 0);
                    //Debug.Assert(wA == 0);
                    wB += iB * Vec2.cross(vcp.rB, P);
                    vB.addLocal(temp.set_Renamed(P).mulLocal(mB));
                    //Debug.Assert(vB.x == 0);
                    //Debug.Assert(wB == 0);
                }
                m_velocities[indexA].w = wA;
                m_velocities[indexB].w = wB;

                //Console.WriteLine("Ending velocity for " + indexA + " is " + vA.x + "," + vA.y + " - " + wA);
                //Console.WriteLine("Ending velocity for " + indexB + " is " + vB.x + "," + vB.y + " - " + wB);
            }
        }

        // djm pooling, and from above
        private readonly Transform xfA = new Transform();
        private readonly Transform xfB = new Transform();
        private readonly WorldManifold worldManifold = new WorldManifold();

        public void initializeVelocityConstraints()
        {
            //Console.WriteLine("Initializing velocity constraints for " + m_count + " contacts");
            // Warm start.
            for (int i = 0; i < m_count; ++i)
            {
                ContactVelocityConstraint vc = m_velocityConstraints[i];
                ContactPositionConstraint pc = m_positionConstraints[i];

                float radiusA = pc.radiusA;
                float radiusB = pc.radiusB;
                Manifold manifold = m_contacts[vc.contactIndex].Manifold;

                int indexA = vc.indexA;
                int indexB = vc.indexB;

                float mA = vc.invMassA;
                float mB = vc.invMassB;
                float iA = vc.invIA;
                float iB = vc.invIB;
                Vec2 localCenterA = pc.localCenterA;
                Vec2 localCenterB = pc.localCenterB;

                Vec2 cA = m_positions[indexA].c;
                float aA = m_positions[indexA].a;
                Vec2 vA = m_velocities[indexA].v;
                float wA = m_velocities[indexA].w;

                Vec2 cB = m_positions[indexB].c;
                float aB = m_positions[indexB].a;
                Vec2 vB = m_velocities[indexB].v;
                float wB = m_velocities[indexB].w;

                Debug.Assert(manifold.pointCount > 0);

                xfA.q.set_Renamed(aA);
                xfB.q.set_Renamed(aB);
                Rot.mulToOutUnsafe(xfA.q, localCenterA, temp);
                xfA.p.set_Renamed(cA).subLocal(temp);
                Rot.mulToOutUnsafe(xfB.q, localCenterB, temp);
                xfB.p.set_Renamed(cB).subLocal(temp);

                worldManifold.initialize(manifold, xfA, radiusA, xfB, radiusB);

                vc.normal.set_Renamed(worldManifold.normal);

                int pointCount = vc.pointCount;
                for (int j = 0; j < pointCount; ++j)
                {
                    ContactVelocityConstraint.VelocityConstraintPoint vcp = vc.points[j];

                    vcp.rA.set_Renamed(worldManifold.points[j]).subLocal(cA);
                    vcp.rB.set_Renamed(worldManifold.points[j]).subLocal(cB);

                    float rnA = Vec2.cross(vcp.rA, vc.normal);
                    float rnB = Vec2.cross(vcp.rB, vc.normal);

                    float kNormal = mA + mB + iA * rnA * rnA + iB * rnB * rnB;

                    vcp.normalMass = kNormal > 0.0f ? 1.0f / kNormal : 0.0f;

                    Vec2.crossToOutUnsafe(vc.normal, 1.0f, tangent);

                    float rtA = Vec2.cross(vcp.rA, tangent);
                    float rtB = Vec2.cross(vcp.rB, tangent);

                    float kTangent = mA + mB + iA * rtA * rtA + iB * rtB * rtB;

                    vcp.tangentMass = kTangent > 0.0f ? 1.0f / kTangent : 0.0f;

                    // Setup a velocity bias for restitution.
                    vcp.velocityBias = 0.0f;
                    Vec2.crossToOutUnsafe(wB, vcp.rB, temp1);
                    Vec2.crossToOutUnsafe(wA, vcp.rA, temp2);
                    temp.set_Renamed(vB).addLocal(temp1).subLocal(vA).subLocal(temp2);
                    float vRel = Vec2.dot(vc.normal, temp);
                    if (vRel < -Settings.velocityThreshold)
                    {
                        vcp.velocityBias = (-vc.restitution) * vRel;
                    }
                }

                // If we have two points, then prepare the block solver.
                if (vc.pointCount == 2)
                {
                    ContactVelocityConstraint.VelocityConstraintPoint vcp1 = vc.points[0];
                    ContactVelocityConstraint.VelocityConstraintPoint vcp2 = vc.points[1];

                    float rn1A = Vec2.cross(vcp1.rA, vc.normal);
                    float rn1B = Vec2.cross(vcp1.rB, vc.normal);
                    float rn2A = Vec2.cross(vcp2.rA, vc.normal);
                    float rn2B = Vec2.cross(vcp2.rB, vc.normal);

                    float k11 = mA + mB + iA * rn1A * rn1A + iB * rn1B * rn1B;
                    float k22 = mA + mB + iA * rn2A * rn2A + iB * rn2B * rn2B;
                    float k12 = mA + mB + iA * rn1A * rn2A + iB * rn1B * rn2B;
                    if (k11 * k11 < k_maxConditionNumber * (k11 * k22 - k12 * k12))
                    {
                        // K is safe to invert.
                        vc.K.ex.set_Renamed(k11, k12);
                        vc.K.ey.set_Renamed(k12, k22);
                        vc.K.invertToOut(vc.normalMass);
                    }
                    else
                    {
                        // The constraints are redundant, just use one.
                        // TODO_ERIN use deepest?
                        vc.pointCount = 1;
                    }
                }
            }
        }

        // djm pooling from above
        private readonly Vec2 dv = new Vec2();
        private readonly Vec2 a = new Vec2();
        private readonly Vec2 b = new Vec2();
        private readonly Vec2 dv1 = new Vec2();
        private readonly Vec2 dv2 = new Vec2();
        private readonly Vec2 x = new Vec2();
        private readonly Vec2 d = new Vec2();
        private readonly Vec2 P1 = new Vec2();
        private readonly Vec2 P2 = new Vec2();

        public void solveVelocityConstraints()
        {
            for (int i = 0; i < m_count; ++i)
            {
                ContactVelocityConstraint vc = m_velocityConstraints[i];

                int indexA = vc.indexA;
                int indexB = vc.indexB;

                float mA = vc.invMassA;
                float mB = vc.invMassB;
                float iA = vc.invIA;
                float iB = vc.invIB;
                int pointCount = vc.pointCount;

                Vec2 vA = m_velocities[indexA].v;
                float wA = m_velocities[indexA].w;
                Vec2 vB = m_velocities[indexB].v;
                float wB = m_velocities[indexB].w;
                //Debug.Assert(wA == 0);
                //Debug.Assert(wB == 0);

                Vec2 normal = vc.normal;
                //Vec2.crossToOutUnsafe(normal, 1f, tangent);
                tangent.x = 1.0f * vc.normal.y;
                tangent.y = (-1.0f) * vc.normal.x;
                float friction = vc.friction;

                Debug.Assert(pointCount == 1 || pointCount == 2);

                // Solve tangent constraints
                for (int j = 0; j < pointCount; ++j)
                {
                    ContactVelocityConstraint.VelocityConstraintPoint vcp = vc.points[j];
                    //Vec2.crossToOutUnsafe(wA, vcp.rA, temp);
                    //Vec2.crossToOutUnsafe(wB, vcp.rB, dv);
                    //dv.addLocal(vB).subLocal(vA).subLocal(temp);
                    Vec2 a = vcp.rA;

                    dv.x = (-wB) * vcp.rB.y + vB.x - vA.x + wA * a.y;
                    dv.y = wB * vcp.rB.x + vB.y - vA.y - wA * a.x;

                    // Compute tangent force
                    float vt = dv.x * tangent.x + dv.y * tangent.y - vc.tangentSpeed;
                    float lambda = vcp.tangentMass * (-vt);

                    // Clamp the accumulated force
                    float maxFriction = friction * vcp.normalImpulse;
                    float newImpulse = MathUtils.clamp(vcp.tangentImpulse + lambda, -maxFriction, maxFriction);
                    lambda = newImpulse - vcp.tangentImpulse;
                    vcp.tangentImpulse = newImpulse;

                    // Apply contact impulse
                    // Vec2 P = lambda * tangent;

                    float Px = tangent.x * lambda;
                    float Py = tangent.y * lambda;

                    // vA -= invMassA * P;
                    vA.x -= Px * mA;
                    vA.y -= Py * mA;
                    wA -= iA * (vcp.rA.x * Py - vcp.rA.y * Px);

                    // vB += invMassB * P;
                    vB.x += Px * mB;
                    vB.y += Py * mB;
                    wB += iB * (vcp.rB.x * Py - vcp.rB.y * Px);

                    //Console.WriteLine("tangent solve velocity (point "+j+") for " + indexA + " is " + vA.x + "," + vA.y + " rot " + wA);
                    //Console.WriteLine("tangent solve velocity (point "+j+") for " + indexB + " is " + vB.x + "," + vB.y + " rot " + wB);
                }

                // Solve normal constraints
                if (vc.pointCount == 1)
                {
                    ContactVelocityConstraint.VelocityConstraintPoint vcp = vc.points[0];
                    Vec2 a1 = vcp.rA;

                    // Relative velocity at contact
                    //Vec2 dv = vB + Cross(wB, vcp.rB) - vA - Cross(wA, vcp.rA);

                    //Vec2.crossToOut(wA, vcp.rA, temp1);
                    //Vec2.crossToOut(wB, vcp.rB, dv);
                    //dv.addLocal(vB).subLocal(vA).subLocal(temp1);

                    dv.x = (-wB) * vcp.rB.y + vB.x - vA.x + wA * a1.y;
                    dv.y = wB * vcp.rB.x + vB.y - vA.y - wA * a1.x;

                    // Compute normal impulse
                    float vn = dv.x * normal.x + dv.y * normal.y;
                    float lambda = (-vcp.normalMass) * (vn - vcp.velocityBias);

                    // Clamp the accumulated impulse
                    float a = vcp.normalImpulse + lambda;
                    float newImpulse = (a > 0.0f ? a : 0.0f);
                    lambda = newImpulse - vcp.normalImpulse;
                    //Debug.Assert(newImpulse == 0);
                    vcp.normalImpulse = newImpulse;

                    // Apply contact impulse
                    float Px = normal.x * lambda;
                    float Py = normal.y * lambda;

                    // vA -= invMassA * P;
                    vA.x -= Px * mA;
                    vA.y -= Py * mA;
                    wA -= iA * (vcp.rA.x * Py - vcp.rA.y * Px);
                    //Debug.Assert(vA.x == 0);

                    // vB += invMassB * P;
                    vB.x += Px * mB;
                    vB.y += Py * mB;
                    wB += iB * (vcp.rB.x * Py - vcp.rB.y * Px);
                    //Debug.Assert(vB.x == 0);
                }
                else
                {
                    // Block solver developed in collaboration with Dirk Gregorius (back in 01/07 on
                    // Box2D_Lite).
                    // Build the mini LCP for this contact patch
                    //
                    // vn = A * x + b, vn >= 0, , vn >= 0, x >= 0 and vn_i * x_i = 0 with i = 1..2
                    //
                    // A = J * W * JT and J = ( -n, -r1 x n, n, r2 x n )
                    // b = vn_0 - velocityBias
                    //
                    // The system is solved using the "Total enumeration method" (s. Murty). The complementary
                    // constraint vn_i * x_i
                    // implies that we must have in any solution either vn_i = 0 or x_i = 0. So for the 2D
                    // contact problem the cases
                    // vn1 = 0 and vn2 = 0, x1 = 0 and x2 = 0, x1 = 0 and vn2 = 0, x2 = 0 and vn1 = 0 need to be
                    // tested. The first valid
                    // solution that satisfies the problem is chosen.
                    //
                    // In order to account of the accumulated impulse 'a' (because of the iterative nature of
                    // the solver which only requires
                    // that the accumulated impulse is clamped and not the incremental impulse) we change the
                    // impulse variable (x_i).
                    //
                    // Substitute:
                    //
                    // x = a + d
                    //
                    // a := old total impulse
                    // x := new total impulse
                    // d := incremental impulse
                    //
                    // For the current iteration we extend the formula for the incremental impulse
                    // to compute the new total impulse:
                    //
                    // vn = A * d + b
                    // = A * (x - a) + b
                    // = A * x + b - A * a
                    // = A * x + b'
                    // b' = b - A * a;

                    ContactVelocityConstraint.VelocityConstraintPoint cp1 = vc.points[0];
                    ContactVelocityConstraint.VelocityConstraintPoint cp2 = vc.points[1];
                    a.x = cp1.normalImpulse;
                    a.y = cp2.normalImpulse;

                    Debug.Assert(a.x >= 0.0f && a.y >= 0.0f);
                    // Relative velocity at contact
                    // Vec2 dv1 = vB + Cross(wB, cp1.rB) - vA - Cross(wA, cp1.rA);
                    dv1.x = (-wB) * cp1.rB.y + vB.x - vA.x + wA * cp1.rA.y;
                    dv1.y = wB * cp1.rB.x + vB.y - vA.y - wA * cp1.rA.x;

                    // Vec2 dv2 = vB + Cross(wB, cp2.rB) - vA - Cross(wA, cp2.rA);
                    dv2.x = (-wB) * cp2.rB.y + vB.x - vA.x + wA * cp2.rA.y;
                    dv2.y = wB * cp2.rB.x + vB.y - vA.y - wA * cp2.rA.x;

                    // Compute normal velocity
                    float vn1 = dv1.x * normal.x + dv1.y * normal.y;
                    float vn2 = dv2.x * normal.x + dv2.y * normal.y;

                    b.x = vn1 - cp1.velocityBias;
                    b.y = vn2 - cp2.velocityBias;
                    //Console.WriteLine("b is " + b.x + "," + b.y);

                    // Compute b'
                    Mat22 R = vc.K;
                    b.x -= (R.ex.x * a.x + R.ey.x * a.y);
                    b.y -= (R.ex.y * a.x + R.ey.y * a.y);
                    //Console.WriteLine("b' is " + b.x + "," + b.y);

                    // final float k_errorTol = 1e-3f;
                    // B2_NOT_USED(k_errorTol);
                    for (; ; )
                    {
                        //
                        // Case 1: vn = 0
                        //
                        // 0 = A * x' + b'
                        //
                        // Solve for x':
                        //
                        // x' = - inv(A) * b'
                        //
                        // Vec2 x = - Mul(c.normalMass, b);
                        Mat22.mulToOutUnsafe(vc.normalMass, b, x);
                        x.mulLocal(-1);

                        if (x.x >= 0.0f && x.y >= 0.0f)
                        {
                            //Console.WriteLine("case 1");
                            // Get the incremental impulse
                            // Vec2 d = x - a;
                            d.set_Renamed(x).subLocal(a);

                            // Apply incremental impulse
                            // Vec2 P1 = d.x * normal;
                            // Vec2 P2 = d.y * normal;
                            P1.set_Renamed(normal).mulLocal(d.x);
                            P2.set_Renamed(normal).mulLocal(d.y);

                            /*
                            * vA -= invMassA * (P1 + P2); wA -= invIA * (Cross(cp1.rA, P1) + Cross(cp2.rA, P2));
                            * 
                            * vB += invMassB * (P1 + P2); wB += invIB * (Cross(cp1.rB, P1) + Cross(cp2.rB, P2));
                            */

                            temp1.set_Renamed(P1).addLocal(P2);
                            temp2.set_Renamed(temp1).mulLocal(mA);
                            vA.subLocal(temp2);
                            temp2.set_Renamed(temp1).mulLocal(mB);
                            vB.addLocal(temp2);
                            //Debug.Assert(vA.x == 0);
                            //Debug.Assert(vB.x == 0);

                            wA -= iA * (Vec2.cross(cp1.rA, P1) + Vec2.cross(cp2.rA, P2));
                            wB += iB * (Vec2.cross(cp1.rB, P1) + Vec2.cross(cp2.rB, P2));

                            // Accumulate
                            cp1.normalImpulse = x.x;
                            cp2.normalImpulse = x.y;

                            /*
                            * #if B2_DEBUG_SOLVER == 1 // Postconditions dv1 = vB + Cross(wB, cp1.rB) - vA -
                            * Cross(wA, cp1.rA); dv2 = vB + Cross(wB, cp2.rB) - vA - Cross(wA, cp2.rA);
                            * 
                            * // Compute normal velocity vn1 = Dot(dv1, normal); vn2 = Dot(dv2, normal);
                            * 
                            * Debug.Assert(Abs(vn1 - cp1.velocityBias) < k_errorTol); Debug.Assert(Abs(vn2 - cp2.velocityBias)
                            * < k_errorTol); #endif
                            */
                            if (DEBUG_SOLVER)
                            {
                                // Postconditions
                                Vec2 _dv1 = vB.add(Vec2.cross(wB, cp1.rB).subLocal(vA).subLocal(Vec2.cross(wA, cp1.rA)));
                                Vec2 _dv2 = vB.add(Vec2.cross(wB, cp2.rB).subLocal(vA).subLocal(Vec2.cross(wA, cp2.rA)));
                                // Compute normal velocity
                                vn1 = Vec2.dot(_dv1, normal);
                                vn2 = Vec2.dot(_dv2, normal);

                                Debug.Assert(MathUtils.abs(vn1 - cp1.velocityBias) < k_errorTol);
                                Debug.Assert(MathUtils.abs(vn2 - cp2.velocityBias) < k_errorTol);
                            }
                            break;
                        }

                        //
                        // Case 2: vn1 = 0 and x2 = 0
                        //
                        // 0 = a11 * x1' + a12 * 0 + b1'
                        // vn2 = a21 * x1' + a22 * 0 + '
                        //
                        x.x = (-cp1.normalMass) * b.x;
                        x.y = 0.0f;
                        vn1 = 0.0f;
                        vn2 = vc.K.ex.y * x.x + b.y;

                        if (x.x >= 0.0f && vn2 >= 0.0f)
                        {
                            //Console.WriteLine("case 2");
                            // Get the incremental impulse
                            d.set_Renamed(x).subLocal(a);

                            // Apply incremental impulse
                            // Vec2 P1 = d.x * normal;
                            // Vec2 P2 = d.y * normal;
                            P1.set_Renamed(normal).mulLocal(d.x);
                            P2.set_Renamed(normal).mulLocal(d.y);

                            /*
                            * Vec2 P1 = d.x * normal; Vec2 P2 = d.y * normal; vA -= invMassA * (P1 + P2); wA -=
                            * invIA * (Cross(cp1.rA, P1) + Cross(cp2.rA, P2));
                            * 
                            * vB += invMassB * (P1 + P2); wB += invIB * (Cross(cp1.rB, P1) + Cross(cp2.rB, P2));
                            */

                            temp1.set_Renamed(P1).addLocal(P2);
                            temp2.set_Renamed(temp1).mulLocal(mA);
                            vA.subLocal(temp2);
                            temp2.set_Renamed(temp1).mulLocal(mB);
                            vB.addLocal(temp2);
                            //Debug.Assert(vA.x == 0);
                            //Debug.Assert(vB.x == 0);

                            wA -= iA * (Vec2.cross(cp1.rA, P1) + Vec2.cross(cp2.rA, P2));
                            wB += iB * (Vec2.cross(cp1.rB, P1) + Vec2.cross(cp2.rB, P2));


                            // Accumulate
                            //Debug.Assert(x.x == 0 && x.y == 0);
                            cp1.normalImpulse = x.x;
                            cp2.normalImpulse = x.y;

                            /*
                            * #if B2_DEBUG_SOLVER == 1 // Postconditions dv1 = vB + Cross(wB, cp1.rB) - vA -
                            * Cross(wA, cp1.rA);
                            * 
                            * // Compute normal velocity vn1 = Dot(dv1, normal);
                            * 
                            * Debug.Assert(Abs(vn1 - cp1.velocityBias) < k_errorTol); #endif
                            */
                            if (DEBUG_SOLVER)
                            {
                                // Postconditions
                                Vec2 _dv1 = vB.add(Vec2.cross(wB, cp1.rB).subLocal(vA).subLocal(Vec2.cross(wA, cp1.rA)));
                                // Compute normal velocity
                                vn1 = Vec2.dot(_dv1, normal);

                                Debug.Assert(MathUtils.abs(vn1 - cp1.velocityBias) < k_errorTol);
                            }
                            break;
                        }


                        //
                        // Case 3: wB = 0 and x1 = 0
                        //
                        // vn1 = a11 * 0 + a12 * x2' + b1'
                        // 0 = a21 * 0 + a22 * x2' + '
                        //
                        x.x = 0.0f;
                        x.y = (-cp2.normalMass) * b.y;
                        vn1 = vc.K.ey.x * x.y + b.x;
                        vn2 = 0.0f;

                        if (x.y >= 0.0f && vn1 >= 0.0f)
                        {
                            //Console.WriteLine("case 3");
                            // Resubstitute for the incremental impulse
                            d.set_Renamed(x).subLocal(a);

                            // Apply incremental impulse
                            /*
                            * Vec2 P1 = d.x * normal; Vec2 P2 = d.y * normal; vA -= invMassA * (P1 + P2); wA -=
                            * invIA * (Cross(cp1.rA, P1) + Cross(cp2.rA, P2));
                            * 
                            * vB += invMassB * (P1 + P2); wB += invIB * (Cross(cp1.rB, P1) + Cross(cp2.rB, P2));
                            */

                            P1.set_Renamed(normal).mulLocal(d.x);
                            P2.set_Renamed(normal).mulLocal(d.y);

                            temp1.set_Renamed(P1).addLocal(P2);
                            temp2.set_Renamed(temp1).mulLocal(mA);
                            vA.subLocal(temp2);
                            temp2.set_Renamed(temp1).mulLocal(mB);
                            vB.addLocal(temp2);
                            //Debug.Assert(vA.x == 0);
                            //Debug.Assert(vB.x == 0);

                            wA -= iA * (Vec2.cross(cp1.rA, P1) + Vec2.cross(cp2.rA, P2));
                            wB += iB * (Vec2.cross(cp1.rB, P1) + Vec2.cross(cp2.rB, P2));

                            // Accumulate
                            //Debug.Assert(x.x == 0 && x.y == 0);
                            cp1.normalImpulse = x.x;
                            cp2.normalImpulse = x.y;

                            /*
                            * #if B2_DEBUG_SOLVER == 1 // Postconditions dv2 = vB + Cross(wB, cp2.rB) - vA -
                            * Cross(wA, cp2.rA);
                            * 
                            * // Compute normal velocity vn2 = Dot(dv2, normal);
                            * 
                            * Debug.Assert(Abs(vn2 - cp2.velocityBias) < k_errorTol); #endif
                            */
                            if (DEBUG_SOLVER)
                            {
                                // Postconditions
                                Vec2 _dv2 =
                                    vB.add(Vec2.cross(wB, cp2.rB).subLocal(vA).subLocal(Vec2.cross(wA, cp2.rA)));
                                // Compute normal velocity
                                vn2 = Vec2.dot(_dv2, normal);

                                Debug.Assert(MathUtils.abs(vn2 - cp2.velocityBias) < k_errorTol);
                            }
                            break;
                        }

                        //
                        // Case 4: x1 = 0 and x2 = 0
                        //
                        // vn1 = b1
                        // vn2 = ;
                        x.x = 0.0f;
                        x.y = 0.0f;
                        vn1 = b.x;
                        vn2 = b.y;

                        if (vn1 >= 0.0f && vn2 >= 0.0f)
                        {
                            //Console.WriteLine("case 4");
                            // Resubstitute for the incremental impulse
                            d.set_Renamed(x).subLocal(a);

                            // Apply incremental impulse
                            /*
                            * Vec2 P1 = d.x * normal; Vec2 P2 = d.y * normal; vA -= invMassA * (P1 + P2); wA -=
                            * invIA * (Cross(cp1.rA, P1) + Cross(cp2.rA, P2));
                            * 
                            * vB += invMassB * (P1 + P2); wB += invIB * (Cross(cp1.rB, P1) + Cross(cp2.rB, P2));
                            */

                            P1.set_Renamed(normal).mulLocal(d.x);
                            P2.set_Renamed(normal).mulLocal(d.y);

                            temp1.set_Renamed(P1).addLocal(P2);
                            temp2.set_Renamed(temp1).mulLocal(mA);
                            vA.subLocal(temp2);
                            temp2.set_Renamed(temp1).mulLocal(mB);
                            vB.addLocal(temp2);
                            //Debug.Assert(vA.x == 0);
                            //Debug.Assert(vB.x == 0);

                            wA -= iA * (Vec2.cross(cp1.rA, P1) + Vec2.cross(cp2.rA, P2));
                            wB += iB * (Vec2.cross(cp1.rB, P1) + Vec2.cross(cp2.rB, P2));


                            // Accumulate
                            //Debug.Assert(x.x == 0 && x.y == 0);
                            cp1.normalImpulse = x.x;
                            cp2.normalImpulse = x.y;

                            break;
                        }

                        // No solution, give up. This is hit sometimes, but it doesn't seem to matter.
                        break;
                    }
                }

                m_velocities[indexA].v.set_Renamed(vA);
                m_velocities[indexA].w = wA;
                m_velocities[indexB].v.set_Renamed(vB);
                m_velocities[indexB].w = wB;

                //Console.WriteLine("Ending velocity for " + indexA + " is " + vA.x + "," + vA.y + " rot " + wA);
                //Console.WriteLine("Ending velocity for " + indexB + " is " + vB.x + "," + vB.y + " rot " + wB);
            }
        }

        public virtual void storeImpulses()
        {
            for (int i = 0; i < m_count; i++)
            {
                ContactVelocityConstraint vc = m_velocityConstraints[i];
                Manifold manifold = m_contacts[vc.contactIndex].Manifold;

                for (int j = 0; j < vc.pointCount; j++)
                {
                    manifold.points[j].normalImpulse = vc.points[j].normalImpulse;
                    manifold.points[j].tangentImpulse = vc.points[j].tangentImpulse;
                }
            }
        }

        /*
        * #if 0 // Sequential solver. bool ContactSolver::SolvePositionConstraints(float baumgarte) {
        * float minSeparation = 0.0f;
        * 
        * for (int i = 0; i < m_constraintCount; ++i) { ContactConstraint* c = m_constraints + i; Body*
        * bodyA = c.bodyA; Body* bodyB = c.bodyB; float invMassA = bodyA.m_mass * bodyA.m_invMass; float
        * invIA = bodyA.m_mass * bodyA.m_invI; float invMassB = bodyB.m_mass * bodyB.m_invMass; float
        * invIB = bodyB.m_mass * bodyB.m_invI;
        * 
        * Vec2 normal = c.normal;
        * 
        * // Solve normal constraints for (int j = 0; j < c.pointCount; ++j) { ContactConstraintPoint*
        * ccp = c.points + j;
        * 
        * Vec2 r1 = Mul(bodyA.GetXForm().R, ccp.localAnchorA - bodyA.GetLocalCenter()); Vec2 r2 =
        * Mul(bodyB.GetXForm().R, ccp.localAnchorB - bodyB.GetLocalCenter());
        * 
        * Vec2 p1 = bodyA.m_sweep.c + r1; Vec2 p2 = bodyB.m_sweep.c + r2; Vec2 dp = p2 - p1;
        * 
        * // Approximate the current separation. float separation = Dot(dp, normal) + ccp.separation;
        * 
        * // Track max constraint error. minSeparation = Min(minSeparation, separation);
        * 
        * // Prevent large corrections and allow slop. float C = Clamp(baumgarte * (separation +
        * _linearSlop), -_maxLinearCorrection, 0.0f);
        * 
        * // Compute normal impulse float impulse = -ccp.equalizedMass * C;
        * 
        * Vec2 P = impulse * normal;
        * 
        * bodyA.m_sweep.c -= invMassA * P; bodyA.m_sweep.a -= invIA * Cross(r1, P);
        * bodyA.SynchronizeTransform();
        * 
        * bodyB.m_sweep.c += invMassB * P; bodyB.m_sweep.a += invIB * Cross(r2, P);
        * bodyB.SynchronizeTransform(); } }
        * 
        * // We can't expect minSpeparation >= -_linearSlop because we don't // push the separation above
        * -_linearSlop. return minSeparation >= -1.5f * _linearSlop; }
        */

        // djm pooling, and from above
        private readonly PositionSolverManifold psolver = new PositionSolverManifold();
        private readonly Vec2 rA = new Vec2();
        private readonly Vec2 rB = new Vec2();

        /// <summary>
        /// Sequential solver.
        /// </summary>
        public bool solvePositionConstraints()
        {
            float minSeparation = 0.0f;

            for (int i = 0; i < m_count; ++i)
            {
                ContactPositionConstraint pc = m_positionConstraints[i];

                int indexA = pc.indexA;
                int indexB = pc.indexB;

                float mA = pc.invMassA;
                float iA = pc.invIA;
                Vec2 localCenterA = pc.localCenterA;
                float mB = pc.invMassB;
                float iB = pc.invIB;
                Vec2 localCenterB = pc.localCenterB;
                int pointCount = pc.pointCount;

                Vec2 cA = m_positions[indexA].c;
                float aA = m_positions[indexA].a;
                Vec2 cB = m_positions[indexB].c;
                float aB = m_positions[indexB].a;
                //Console.WriteLine("cA: " + cA.x + "," + cA.y + " - rot " + aA);
                //Console.WriteLine("cB: " + cB.x + "," + cB.y + " - rot " + aB);

                // Solve normal constraints
                for (int j = 0; j < pointCount; ++j)
                {
                    xfA.q.set_Renamed(aA);
                    xfB.q.set_Renamed(aB);
                    Rot.mulToOutUnsafe(xfA.q, localCenterA, xfA.p);
                    xfA.p.negateLocal().addLocal(cA);
                    Rot.mulToOutUnsafe(xfB.q, localCenterB, xfB.p);
                    xfB.p.negateLocal().addLocal(cB);

                    PositionSolverManifold psm = psolver;
                    psm.initialize(pc, xfA, xfB, j);
                    Vec2 normal = psm.normal;

                    Vec2 point = psm.point;
                    float separation = psm.separation;

                    rA.set_Renamed(point).subLocal(cA);
                    rB.set_Renamed(point).subLocal(cB);

                    // Track max constraint error.
                    minSeparation = MathUtils.min(minSeparation, separation);

                    // Prevent large corrections and allow slop.
                    float C = MathUtils.clamp(Settings.baumgarte * (separation + Settings.linearSlop), -Settings.maxLinearCorrection, 0.0f);

                    // Compute the effective mass.
                    float rnA = Vec2.cross(rA, normal);
                    float rnB = Vec2.cross(rB, normal);
                    float K = mA + mB + iA * rnA * rnA + iB * rnB * rnB;

                    // Compute normal impulse
                    float impulse = K > 0.0f ? (-C) / K : 0.0f;

                    P.set_Renamed(normal).mulLocal(impulse);

                    cA.subLocal(temp.set_Renamed(P).mulLocal(mA));
                    aA -= iA * Vec2.cross(rA, P);


                    cB.addLocal(temp.set_Renamed(P).mulLocal(mB));
                    aB += iB * Vec2.cross(rB, P);
                }

                m_positions[indexA].c.set_Renamed(cA);
                m_positions[indexA].a = aA;

                m_positions[indexB].c.set_Renamed(cB);
                m_positions[indexB].a = aB;
                //Console.WriteLine("ending pos "+indexA+": " + cA.x + "," + cA.y + " - rot " + aA);
                //Console.WriteLine("ending pos "+indexB+": " + cB.x + "," + cB.y + " - rot " + aB);
            }

            // We can't expect minSpeparation >= -linearSlop because we don't
            // push the separation above -linearSlop.
            return minSeparation >= (-3.0f) * Settings.linearSlop;
        }

        // Sequential position solver for position constraints.
        public virtual bool solveTOIPositionConstraints(int toiIndexA, int toiIndexB)
        {
            float minSeparation = 0.0f;

            for (int i = 0; i < m_count; ++i)
            {
                ContactPositionConstraint pc = m_positionConstraints[i];

                int indexA = pc.indexA;
                int indexB = pc.indexB;
                Vec2 localCenterA = pc.localCenterA;
                Vec2 localCenterB = pc.localCenterB;
                int pointCount = pc.pointCount;

                float mA = 0.0f;
                float iA = 0.0f;
                if (indexA == toiIndexA || indexA == toiIndexB)
                {
                    mA = pc.invMassA;
                    iA = pc.invIA;
                }

                float mB = pc.invMassB;
                float iB = pc.invIB;
                if (indexB == toiIndexA || indexB == toiIndexB)
                {
                    mB = pc.invMassB;
                    iB = pc.invIB;
                }

                Vec2 cA = m_positions[indexA].c;
                float aA = m_positions[indexA].a;

                Vec2 cB = m_positions[indexB].c;
                float aB = m_positions[indexB].a;

                // Solve normal constraints
                for (int j = 0; j < pointCount; ++j)
                {
                    xfA.q.set_Renamed(aA);
                    xfB.q.set_Renamed(aB);
                    Rot.mulToOutUnsafe(xfA.q, localCenterA, xfA.p);
                    xfA.p.negateLocal().addLocal(cA);
                    Rot.mulToOutUnsafe(xfB.q, localCenterB, xfB.p);
                    xfB.p.negateLocal().addLocal(cB);

                    PositionSolverManifold psm = psolver;
                    psm.initialize(pc, xfA, xfB, j);
                    Vec2 normal = psm.normal;

                    Vec2 point = psm.point;
                    float separation = psm.separation;

                    rA.set_Renamed(point).subLocal(cA);
                    rB.set_Renamed(point).subLocal(cB);

                    // Track max constraint error.
                    minSeparation = MathUtils.min(minSeparation, separation);

                    // Prevent large corrections and allow slop.
                    float C = MathUtils.clamp(Settings.toiBaugarte * (separation + Settings.linearSlop), -Settings.maxLinearCorrection, 0.0f);

                    // Compute the effective mass.
                    float rnA = Vec2.cross(rA, normal);
                    float rnB = Vec2.cross(rB, normal);
                    float K = mA + mB + iA * rnA * rnA + iB * rnB * rnB;

                    // Compute normal impulse
                    float impulse = K > 0.0f ? (-C) / K : 0.0f;

                    P.set_Renamed(normal).mulLocal(impulse);

                    cA.subLocal(temp.set_Renamed(P).mulLocal(mA));
                    aA -= iA * Vec2.cross(rA, P);

                    cB.addLocal(temp.set_Renamed(P).mulLocal(mB));
                    aB += iB * Vec2.cross(rB, P);
                }

                m_positions[indexA].c.set_Renamed(cA);
                m_positions[indexA].a = aA;

                m_positions[indexB].c.set_Renamed(cB);
                m_positions[indexB].a = aB;
            }

            // We can't expect minSpeparation >= -_linearSlop because we don't
            // push the separation above -_linearSlop.
            return minSeparation >= (-1.5f) * Settings.linearSlop;
        }

        public class ContactSolverDef
        {
            public TimeStep step;
            public Contact[] contacts;
            public int count;
            public Position[] positions;
            public Velocity[] velocities;
        }
    }



    class PositionSolverManifold
    {

        public readonly Vec2 normal = new Vec2();
        public readonly Vec2 point = new Vec2();
        public float separation;

        // djm pooling
        private readonly Vec2 pointA = new Vec2();
        private readonly Vec2 pointB = new Vec2();
        private readonly Vec2 temp = new Vec2();
        private readonly Vec2 planePoint = new Vec2();
        private readonly Vec2 clipPoint = new Vec2();

        public virtual void initialize(ContactPositionConstraint pc, Transform xfA, Transform xfB, int index)
        {
            Debug.Assert(pc.pointCount > 0);

            switch (pc.type)
            {

                case Manifold.ManifoldType.CIRCLES:
                    {
                        Transform.mulToOutUnsafe(xfA, pc.localPoint, pointA);
                        Transform.mulToOutUnsafe(xfB, pc.localPoints[0], pointB);
                        normal.set_Renamed(pointB).subLocal(pointA);
                        normal.normalize();

                        point.set_Renamed(pointA).addLocal(pointB).mulLocal(.5f);
                        temp.set_Renamed(pointB).subLocal(pointA);
                        separation = Vec2.dot(temp, normal) - pc.radiusA - pc.radiusB;
                        break;
                    }


                case Manifold.ManifoldType.FACE_A:
                    {
                        Rot.mulToOutUnsafe(xfA.q, pc.localNormal, normal);
                        Transform.mulToOutUnsafe(xfA, pc.localPoint, planePoint);

                        Transform.mulToOutUnsafe(xfB, pc.localPoints[index], clipPoint);
                        temp.set_Renamed(clipPoint).subLocal(planePoint);
                        separation = Vec2.dot(temp, normal) - pc.radiusA - pc.radiusB;
                        point.set_Renamed(clipPoint);
                        break;
                    }


                case Manifold.ManifoldType.FACE_B:
                    {
                        Rot.mulToOutUnsafe(xfB.q, pc.localNormal, normal);
                        Transform.mulToOutUnsafe(xfB, pc.localPoint, planePoint);

                        Transform.mulToOutUnsafe(xfA, pc.localPoints[index], clipPoint);
                        temp.set_Renamed(clipPoint).subLocal(planePoint);
                        separation = Vec2.dot(temp, normal) - pc.radiusA - pc.radiusB;
                        point.set_Renamed(clipPoint);

                        // Ensure normal points from A to B
                        normal.negateLocal();
                    }
                    break;
            }
        }
    }
}