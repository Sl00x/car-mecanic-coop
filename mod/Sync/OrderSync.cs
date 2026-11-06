using System;
using System.Collections.Generic;
using UnityEngine;
using CarMecaSim.Mp.Core;
using CarMecaSim.Mp.Game;

namespace CarMecaSim.Mp.Sync
{
    /// <summary>
    /// Mission/job board sharing. The host broadcasts its available jobs; clients see them in the
    /// co-op panel and can ask the host to accept one. The host accepts it (TakeJob), the car loads
    /// into the shared garage, and everyone sees it. Avoids reconstructing complex Job objects.
    /// </summary>
    public static class OrderSync
    {
        public const byte OpList = 0, OpTake = 1;

        public readonly struct JobInfo
        {
            public readonly int Id; public readonly string Car; public readonly int Pay; public readonly int Xp; public readonly bool Mission; public readonly int MissionId;
            public JobInfo(int id, string car, int pay, int xp, bool mission, int missionId)
            { Id = id; Car = car; Pay = pay; Xp = xp; Mission = mission; MissionId = missionId; }
        }

        /// <summary>Host's available jobs, as seen by clients (for the overlay).</summary>
        public static readonly List<JobInfo> HostJobs = new();

        private static MpSession S => CarMecaPlugin.Session;
        private static bool Active => S != null && S.InLobby && GameRefs.InGameSession;
        private static OrderGenerator _og;
        private static float _lastBroadcast;

        private static OrderGenerator OG()
        {
            try { if (_og == null) _og = UnityEngine.Object.FindObjectOfType<OrderGenerator>(); } catch { }
            return _og;
        }

        public static List<JobInfo> LocalJobs()
        {
            var list = new List<JobInfo>();
            var og = OG(); if (og == null) return list;
            try
            {
                var jobs = og.Jobs;
                if (jobs != null)
                    for (int i = 0; i < jobs.Count; i++)
                    {
                        var j = jobs[i]; if (j == null) continue;
                        bool mission = false; int mid = 0;
                        try { mission = j.IsMission; mid = j.MissionID; } catch { }
                        list.Add(new JobInfo(j.id, j.carFile ?? "", j.TotalPayout, j.XP, mission, mid));
                    }
            }
            catch { }
            return list;
        }

        public static void Tick() { if (Active && S.IsHost && Time.unscaledTime - _lastBroadcast > 3f) BroadcastJobs(); }
        public static void OnPeerJoined() { if (Active && S.IsHost) BroadcastJobs(); }

        public static void BroadcastJobs()
        {
            if (!Active || !S.IsHost) return;
            S.SendJobs(LocalJobs());
            _lastBroadcast = Time.unscaledTime;
        }

        public static void ApplyJobs(List<JobInfo> jobs)
        {
            if (!Active || S.IsHost) return;
            HostJobs.Clear(); HostJobs.AddRange(jobs);
        }

        /// <summary>Client: ask the host to accept a job. Host: accept it directly.</summary>
        public static void Take(JobInfo job)
        {
            if (!Active) return;
            if (S.IsHost) HostTake(job.Id, job.Mission, job.MissionId);
            else S.SendTakeJob(job.Id, job.Mission, job.MissionId);
        }

        public static void HostTake(int id, bool mission, int missionId)
        {
            if (!Active || !S.IsHost) return;
            var og = OG(); if (og == null) return;
            try
            {
                if (mission) og.StartCoroutine(og.TakeMission(missionId, false));
                else og.StartCoroutine(og.TakeJob(id, false));
                CarMecaPlugin.Logger?.LogInfo($"[order] host took {(mission ? "mission " + missionId : "job " + id)}");
                BroadcastJobs();   // refresh the board for clients
            }
            catch (Exception e) { CarMecaPlugin.Logger?.LogWarning("[order] take failed: " + e.Message); }
        }
    }
}
