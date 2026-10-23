namespace RevitMCPCommandSet.Commands.Views
{
    /// <summary>
    /// Wait deadlines for the Interior Elevation commands. They must stay below the MCP server's
    /// 120 s socket deadline (server/src/utils/SocketClient.ts), so the AI gets the command's own
    /// "outcome unknown" timeout message instead of a bare socket timeout.
    ///
    /// Measured (ElevationViewFactoryTests.Create_TwelveViewBatch_Timing, Revit 2026 test runtime,
    /// small test model): a 12-view batch took about 0.5 s, i.e. ~40 ms per view. Each view costs
    /// up to four regenerations (after create, after rotation, after activating the crop, on commit),
    /// so the time grows with model size. On a large client model at ~2 s per regeneration a full
    /// batch is ~12 × 4 × 2 s ≈ 96 s. 100 s covers that and leaves 20 s of the socket deadline for
    /// waiting on Revit to become idle and for transport.
    /// ensure_floor_plan does one regeneration per created plan, so 60 s is ample for it.
    ///
    /// analyze_room_elevations is read-only (no regeneration). Measured
    /// (RoomElevationAnalyzerTests.Analyze_FivePlacedRooms_Timing, same runtime, five simple rooms
    /// analysed 20 times): about 3 ms per room, so the prompts' largest call of 200 rooms takes ~0.6 s
    /// there. This is NOT measured on a client model; room geometry grows with boundary complexity.
    /// 100 s keeps the same 20 s margin to the socket deadline and tolerates rooms ~150× slower than
    /// the test rooms at 200 per call.
    /// </summary>
    internal static class CommandDeadlines
    {
        public const int AnalyzeRoomElevationsMilliseconds = 100_000;
        public const int CreateElevationViewsMilliseconds = 100_000;
        public const int EnsureFloorPlanMilliseconds = 60_000;

        public static TimeoutException Timeout(string command, int milliseconds) =>
            new TimeoutException(
                $"{command} timed out after {milliseconds / 1000} s. The outcome is unknown: Revit may still " +
                "finish the work. Do not retry automatically; check the model first.");
    }
}
