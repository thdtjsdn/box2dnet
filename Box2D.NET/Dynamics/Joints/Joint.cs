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
using Box2D.Common;
using Box2D.Pooling;

namespace Box2D.Dynamics.Joints
{

    // updated to rev 100
    /// <summary>
    /// The base joint class. Joints are used to raint two bodies together in
    /// various fashions. Some joints also feature limits and motors.
    /// </summary>
    /// <author>Daniel Murphy</author>
    public abstract class Joint
    {
        public static Joint create(World argWorld, JointDef def)
        {
            //Joint joint = null;
            switch (def.type)
            {

                case JointType.MOUSE:
                    return new MouseJoint(argWorld.Pool, (MouseJointDef)def);

                case JointType.DISTANCE:
                    return new DistanceJoint(argWorld.Pool, (DistanceJointDef)def);

                case JointType.PRISMATIC:
                    return new PrismaticJoint(argWorld.Pool, (PrismaticJointDef)def);

                case JointType.REVOLUTE:
                    return new RevoluteJoint(argWorld.Pool, (RevoluteJointDef)def);

                case JointType.WELD:
                    return new WeldJoint(argWorld.Pool, (WeldJointDef)def);

                case JointType.FRICTION:
                    return new FrictionJoint(argWorld.Pool, (FrictionJointDef)def);

                //case JointType.WHEEL:
                //    return new WheelJoint(argWorld.Pool, (LineJointDef)def);

                //case JointType.GEAR:
                //    return new GearJoint(argWorld.Pool, (GearJointDef)def);

                case JointType.PULLEY:
                    return new PulleyJoint(argWorld.Pool, (PulleyJointDef)def);

                case JointType.CONSTANT_VOLUME:
                    return new ConstantVolumeJoint(argWorld, (ConstantVolumeJointDef)def);
            }
            return null;
        }

        public static void destroy(Joint joint)
        {
            joint.destructor();
        }

        public JointType m_type;
        public Joint m_prev;
        public Joint m_next;
        public JointEdge m_edgeA;
        public JointEdge m_edgeB;
        public Body m_bodyA;
        public Body m_bodyB;
        public int m_index;

        public bool m_islandFlag;
        public bool m_collideConnected;

        public Object m_userData;

        protected internal IWorldPool pool;

        // Cache here per time step to reduce cache misses.
        //	final Vec2 m_localCenterA, m_localCenterB;
        //	float m_invMassA, m_invIA;
        //	float m_invMassB, m_invIB;

        protected internal Joint(IWorldPool argWorldPool, JointDef def)
        {
            Debug.Assert(def.bodyA != def.bodyB);

            pool = argWorldPool;
            m_type = def.type;
            m_prev = null;
            m_next = null;
            m_bodyA = def.bodyA;
            m_bodyB = def.bodyB;
            m_collideConnected = def.collideConnected;
            m_islandFlag = false;
            m_userData = def.userData;
            m_index = 0;

            m_edgeA = new JointEdge();
            m_edgeA.joint = null;
            m_edgeA.other = null;
            m_edgeA.prev = null;
            m_edgeA.next = null;

            m_edgeB = new JointEdge();
            m_edgeB.joint = null;
            m_edgeB.other = null;
            m_edgeB.prev = null;
            m_edgeB.next = null;

            //		m_localCenterA = new Vec2();
            //		m_localCenterB = new Vec2();
        }

        /// <summary>
        /// get the type of the concrete joint.
        /// </summary>
        /// <returns></returns>
        virtual public JointType Type
        {
            get
            {
                return m_type;
            }
        }

        /// <summary>
        /// get the first body attached to this joint.
        /// </summary>
        virtual public Body BodyA
        {
            get
            {
                return m_bodyA;
            }
        }

        /// <summary>
        /// get the second body attached to this joint.
        /// </summary>
        /// <returns></returns>
        virtual public Body BodyB
        {
            get
            {
                return m_bodyB;
            }
        }

        /// <summary>
        /// get the next joint the world joint list.
        /// </summary>
        virtual public Joint Next
        {
            get
            {
                return m_next;
            }
        }

        /// <summary>
        /// gets or sets the user data pointer.
        /// </summary>
        virtual public object UserData
        {
            get
            {
                return m_userData;
            }
            set
            {
                m_userData = value;
            }
        }

        /// <summary>
        /// Get collide connected.
        /// Note: modifying the collide connect flag won't work correctly because
        /// the flag is only checked when fixture AABBs begin to overlap.
        /// </summary>
        virtual public bool CollideConnected
        {
            get
            {
                return m_collideConnected;
            }
        }


        /// <summary>
        /// get the anchor point on bodyA in world coordinates.
        /// </summary>
        /// <returns></returns>
        public abstract void getAnchorA(Vec2 argOut);

        /// <summary>
        /// get the anchor point on bodyB in world coordinates.
        /// </summary>
        /// <returns></returns>
        public abstract void getAnchorB(Vec2 argOut);

        /// <summary>
        /// get the reaction force on body2 at the joint anchor in Newtons.
        /// </summary>
        /// <param name="inv_dt"></param>
        /// <returns></returns>
        public abstract void getReactionForce(float inv_dt, Vec2 argOut);

        /// <summary>
        /// get the reaction torque on body2 in N*m.
        /// </summary>
        /// <param name="inv_dt"></param>
        /// <returns></returns>
        public abstract float getReactionTorque(float inv_dt);

        /// <summary>
        /// Short-cut function to determine if either body is inactive.
        /// </summary>
        /// <returns></returns>
        public virtual bool IsActive()
        {
            return m_bodyA.Active && m_bodyB.Active;
        }

        public abstract void initVelocityConstraints(SolverData data);

        public abstract void solveVelocityConstraints(SolverData data);

        /// <summary>
        /// This returns true if the position errors are within tolerance.
        /// </summary>
        /// <param name="baumgarte"></param>
        /// <returns></returns>
        public abstract bool solvePositionConstraints(SolverData data);

        /// <summary>
        /// Override to handle destruction of joint
        /// </summary>
        public virtual void destructor()
        {
        }
    }
}