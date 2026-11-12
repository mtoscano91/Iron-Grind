using System;
using System.Collections.Generic;
using System.Reflection;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using Is = NUnit.Framework.Is;

namespace IronGrind.Tests.EditMode.Navigation
{
    /// <summary>Shared fake agent for the Navigation Story 003/004 tests; records calls when given a recorder.</summary>
    internal sealed class FakeNavAgent : INavAgent
    {
        private readonly List<string> _recorder;
        private readonly int _id;

        /// <summary>Creates a fake agent; <paramref name="recorder"/> may be null when call order is not asserted.</summary>
        public FakeNavAgent(int id, List<string> recorder)
        {
            _id = id;
            _recorder = recorder;
        }

        public string Name => "agent" + _id;

        public bool EnabledValue = true;
        public bool UpdatePositionValue = true;
        public bool UpdateRotationValue = true;
        public bool AutoBrakingValue = true;
        public bool ObstacleAvoidanceDisabled;
        public bool IsStoppedValue;
        public float SpeedValue;

        // Scripted Warp result and what Warp saw.
        public bool WarpResult = true;

        // Throw switches for the exception-safety tests.
        public bool ThrowOnWarp;
        public bool ThrowOnResetPath;
        public int WarpCalls;
        public Vector3 LastWarpPosition;
        public int ResetPathCalls;
        public int SetDestinationCalls;

        public bool IsOnNavMesh { get; set; } = true;
        public bool PathPending { get; set; }
        public NavMeshPathStatus PathStatus { get; set; } = NavMeshPathStatus.PathComplete;
        public bool IsPathStale { get; set; }
        public int DesiredVelocityReads;
        private Vector3 _desiredVelocity;
        public Vector3 DesiredVelocity
        {
            get { DesiredVelocityReads++; return _desiredVelocity; }
            set { _desiredVelocity = value; }
        }
        public int NextPositionSets;
        private Vector3 _nextPosition;
        public Vector3 NextPosition
        {
            get => _nextPosition;
            set { _nextPosition = value; NextPositionSets++; Record(Name + ".NextPosition"); }
        }

        public Vector3 LastDestination;
        public bool SetDestinationResult = true;
        public bool ThrowOnSetDestination;

        public bool Enabled
        {
            get => EnabledValue;
            set { EnabledValue = value; Record(Name + ".Enabled=" + value); }
        }

        public bool UpdatePosition
        {
            get => UpdatePositionValue;
            set { UpdatePositionValue = value; Record(Name + ".UpdatePosition=" + value); }
        }

        public bool UpdateRotation
        {
            get => UpdateRotationValue;
            set { UpdateRotationValue = value; Record(Name + ".UpdateRotation=" + value); }
        }

        public bool AutoBraking
        {
            get => AutoBrakingValue;
            set { AutoBrakingValue = value; Record(Name + ".AutoBraking=" + value); }
        }

        public bool IsStopped
        {
            get => IsStoppedValue;
            set { IsStoppedValue = value; Record(Name + ".IsStopped=" + value); }
        }

        public float Speed
        {
            get => SpeedValue;
            set { SpeedValue = value; Record(Name + ".Speed"); }
        }

        public void DisableObstacleAvoidance()
        {
            ObstacleAvoidanceDisabled = true;
            Record(Name + ".ObstacleAvoidance=off");
        }

        public bool Warp(Vector3 position)
        {
            WarpCalls++;
            LastWarpPosition = position;
            Record(Name + ".Warp");
            if (ThrowOnWarp)
            {
                throw new InvalidOperationException(Name + " warp exploded");
            }

            return WarpResult;
        }

        public bool SetDestination(Vector3 target)
        {
            SetDestinationCalls++;
            LastDestination = target;
            Record(Name + ".SetDestination");
            if (ThrowOnSetDestination)
            {
                throw new InvalidOperationException(Name + " setdestination exploded");
            }

            return SetDestinationResult;
        }

        public void ResetPath()
        {
            ResetPathCalls++;
            Record(Name + ".ResetPath");
            if (ThrowOnResetPath)
            {
                throw new InvalidOperationException(Name + " resetpath exploded");
            }
        }

        private void Record(string entry)
        {
            _recorder?.Add(entry);
        }
    }

    /// <summary>Shared fake agent factory; numbers agents from 1 and keeps every created agent.</summary>
    internal sealed class FakeNavAgentFactory : INavAgentFactory
    {
        private readonly List<string> _recorder;

        public readonly List<FakeNavAgent> Created = new List<FakeNavAgent>();
        public int CreateCalls;

        /// <summary>Creates the factory; <paramref name="recorder"/> may be null.</summary>
        public FakeNavAgentFactory(List<string> recorder = null)
        {
            _recorder = recorder;
        }

        public INavAgent Create(string poolRootName)
        {
            CreateCalls++;
            var agent = new FakeNavAgent(CreateCalls, _recorder);
            Created.Add(agent);
            return agent;
        }
    }

    /// <summary>Shared fake NavMesh instance ops: an instance is valid while added; Remove is recorded.</summary>
    internal sealed class FakeNavMeshInstanceOps : INavMeshInstanceOps
    {
        private readonly List<string> _recorder;
        private readonly HashSet<NavMeshDataInstance> _added = new HashSet<NavMeshDataInstance>();

        public int RemoveCalls;
        public Func<string> OnRemoveSnapshot;

        /// <summary>Creates the ops; <paramref name="recorder"/> may be null.</summary>
        public FakeNavMeshInstanceOps(List<string> recorder = null)
        {
            _recorder = recorder;
        }

        public void Add(NavMeshDataInstance instance)
        {
            _added.Add(instance);
        }

        public bool IsValid(NavMeshDataInstance instance) => _added.Contains(instance);

        public void Remove(NavMeshDataInstance instance)
        {
            _added.Remove(instance);
            RemoveCalls++;
            _recorder?.Add(OnRemoveSnapshot != null ? OnRemoveSnapshot() : "Remove");
        }
    }

    /// <summary>Shared fake speed source with per-type speeds; any type not added is unknown.</summary>
    internal sealed class FakeSpeedSource : INavMobSpeedSource
    {
        public readonly Dictionary<MobTypeID, float> Speeds = new Dictionary<MobTypeID, float>();

        public bool TryGetMoveSpeed(MobTypeID mobType, out float speed)
        {
            return Speeds.TryGetValue(mobType, out speed);
        }
    }

    /// <summary>
    /// Shared fake mesh query: every position samples onto the mesh unchanged unless <see cref="Hits"/> says
    /// otherwise. Records every call.
    /// </summary>
    internal sealed class FakeMeshQuery : INavMeshQuery
    {
        public readonly List<Vector3> CalledPositions = new List<Vector3>();
        public readonly List<float> CalledRadii = new List<float>();
        public readonly List<int> CalledAreaMasks = new List<int>();

        /// <summary>Decides whether a position hits; null means always.</summary>
        public Func<Vector3, bool> Hits;

        /// <summary>Added to the position to form the hit position.</summary>
        public Vector3 HitOffset;

        public int CallCount => CalledPositions.Count;

        public bool SamplePosition(Vector3 position, float maxDistance, int areaMask, out Vector3 hitPosition)
        {
            CalledPositions.Add(position);
            CalledRadii.Add(maxDistance);
            CalledAreaMasks.Add(areaMask);
            hitPosition = position + HitOffset;
            return Hits == null || Hits(position);
        }
    }

    /// <summary>Helpers shared by the Navigation tests.</summary>
    internal static class NavigationTestSupport
    {
        /// <summary>
        /// Builds a non-default instance by setting the struct's int backing field, so tests can tell instances apart
        /// (every real instance needs added NavMesh data, which EditMode does not have).
        /// </summary>
        public static NavMeshDataInstance MakeInstance(int id)
        {
            FieldInfo field = null;
            foreach (FieldInfo f in typeof(NavMeshDataInstance).GetFields(
                         BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (f.FieldType == typeof(int))
                {
                    field = f;
                    break;
                }
            }

            Assert.That(field, Is.Not.Null, "NavMeshDataInstance has no int field to set; update MakeInstance");
            object boxed = default(NavMeshDataInstance);
            field.SetValue(boxed, id);
            return (NavMeshDataInstance)boxed;
        }
    }
}
