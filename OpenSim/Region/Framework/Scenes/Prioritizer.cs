﻿using System;
using System.Collections.Generic;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenMetaverse;
using OpenSim.Region.Physics.Manager;

/*
 * Steps to add a new prioritization policy:
 * 
 *  - Add a new value to the UpdatePrioritizationSchemes enum.
 *  - Specify this new value in the [InterestManagement] section of your
 *    OpenSim.ini. The name in the config file must match the enum value name
 *    (although it is not case sensitive).
 *  - Write a new GetPriorityBy*() method in this class.
 *  - Add a new entry to the switch statement in GetUpdatePriority() that calls
 *    your method.
 */

namespace OpenSim.Region.Framework.Scenes
{
    public enum UpdatePrioritizationSchemes
    {
        Time = 0,
        Distance = 1,
        SimpleAngularDistance = 2,
        FrontBack = 3,
        BestAvatarResponsiveness = 4,
    }

    public class Prioritizer
    {
        private static readonly ILog m_log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_scene;

        public Prioritizer(Scene scene)
        {
            m_scene = scene;
        }

        public double GetUpdatePriority(IClientAPI client, ISceneEntity entity)
        {
            switch (m_scene.UpdatePrioritizationScheme)
            {
                case UpdatePrioritizationSchemes.Time:
                    return GetPriorityByTime();
                case UpdatePrioritizationSchemes.Distance:
                    return GetPriorityByDistance(client, entity);
                case UpdatePrioritizationSchemes.SimpleAngularDistance:
                    return GetPriorityByDistance(client, entity); // TODO: Reimplement SimpleAngularDistance
                case UpdatePrioritizationSchemes.FrontBack:
                    return GetPriorityByFrontBack(client, entity);
                case UpdatePrioritizationSchemes.BestAvatarResponsiveness:
                    return GetPriorityByBestAvatarResponsiveness(client, entity);
                default:
                    throw new InvalidOperationException("UpdatePrioritizationScheme not defined.");
            }
        }

        private double GetPriorityByTime()
        {
            return DateTime.UtcNow.ToOADate();
        }

        private double GetPriorityByDistance(IClientAPI client, ISceneEntity entity)
        {
            ScenePresence presence = m_scene.GetScenePresence(client.AgentId);
            if (presence != null)
            {
                // If this is an update for our own avatar give it the highest priority
                if (presence == entity)
                    return 0.0;

                // Use the camera position for local agents and avatar position for remote agents
                Vector3 presencePos = (presence.IsChildAgent) ?
                    presence.AbsolutePosition :
                    presence.CameraPosition;

                // Use group position for child prims
                Vector3 entityPos;
                if (entity is SceneObjectPart)
                    entityPos = m_scene.GetGroupByPrim(entity.LocalId).AbsolutePosition;
                else
                    entityPos = entity.AbsolutePosition;

                return Vector3.DistanceSquared(presencePos, entityPos);
            }

            return double.NaN;
        }

        private double GetPriorityByFrontBack(IClientAPI client, ISceneEntity entity)
        {
            ScenePresence presence = m_scene.GetScenePresence(client.AgentId);
            if (presence != null)
            {
                // If this is an update for our own avatar give it the highest priority
                if (presence == entity)
                    return 0.0;

                // Use group position for child prims
                Vector3 entityPos = entity.AbsolutePosition;
                if (entity is SceneObjectPart)
                    entityPos = m_scene.GetGroupByPrim(entity.LocalId).AbsolutePosition;
                else
                    entityPos = entity.AbsolutePosition;

                if (!presence.IsChildAgent)
                {
                    // Root agent. Use distance from camera and a priority decrease for objects behind us
                    Vector3 camPosition = presence.CameraPosition;
                    Vector3 camAtAxis = presence.CameraAtAxis;

                    // Distance
                    double priority = Vector3.DistanceSquared(camPosition, entityPos);

                    // Plane equation
                    float d = -Vector3.Dot(camPosition, camAtAxis);
                    float p = Vector3.Dot(camAtAxis, entityPos) + d;
                    if (p < 0.0f) priority *= 2.0;

                    return priority;
                }
                else
                {
                    // Child agent. Use the normal distance method
                    Vector3 presencePos = presence.AbsolutePosition;

                    return Vector3.DistanceSquared(presencePos, entityPos);
                }
            }

            return double.NaN;
        }

        private double GetPriorityByBestAvatarResponsiveness(IClientAPI client, ISceneEntity entity)
        {
            ScenePresence presence = m_scene.GetScenePresence(client.AgentId);
            if (presence != null)
            {
                // If this is an update for our own avatar give it the highest priority
                if (presence == entity)
                    return 0.0;

                // Use group position for child prims
                Vector3 entityPos = entity.AbsolutePosition;
                if (entity is SceneObjectPart)
                    entityPos = m_scene.GetGroupByPrim(entity.LocalId).AbsolutePosition;
                else
                    entityPos = entity.AbsolutePosition;

                if (!presence.IsChildAgent)
                {
                    if (entity is ScenePresence)
                        return 1.0;

                    // Root agent. Use distance from camera and a priority decrease for objects behind us
                    Vector3 camPosition = presence.CameraPosition;
                    Vector3 camAtAxis = presence.CameraAtAxis;

                    // Distance
                    double priority = Vector3.DistanceSquared(camPosition, entityPos);

                    // Plane equation
                    float d = -Vector3.Dot(camPosition, camAtAxis);
                    float p = Vector3.Dot(camAtAxis, entityPos) + d;
                    if (p < 0.0f) priority *= 2.0;

                    if (entity is SceneObjectPart)
                    {
                        PhysicsActor physActor = ((SceneObjectPart)entity).ParentGroup.RootPart.PhysActor;
                        if (physActor == null || !physActor.IsPhysical)
                            priority+=100;
                    }
                    return priority;
                }
                else
                {
                    // Child agent. Use the normal distance method
                    Vector3 presencePos = presence.AbsolutePosition;

                    return Vector3.DistanceSquared(presencePos, entityPos);
                }
            }

            return double.NaN;
        }
    }
}
