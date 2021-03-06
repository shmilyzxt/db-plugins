﻿using System.Threading.Tasks;
using Buddy.Coroutines;
using Trinity.DbProvider;
using Trinity.Helpers;
using Zeta.Bot;
using Zeta.Bot.Coroutines;
using Zeta.Bot.Navigation;
using Zeta.Common;
using Zeta.Game;
using Zeta.Game.Internals;
using Zeta.Game.Internals.Actors;
using Zeta.TreeSharp;
using Logger = Trinity.Technicals.Logger;

namespace TrinityCoroutines
{
    public class MoveTo
    {
        /// <summary>
        /// Moves to somewhere.
        /// </summary>
        /// <param name="location">where to move to</param>
        /// <param name="destinationName">name of location for debugging purposes</param>
        /// <param name="range">how close it should get</param>
        public static async Task<bool> Execute(Vector3 location, string destinationName = "", float range = 10f)
        {
            while (ZetaDia.IsInGame && location.Distance(ZetaDia.Me.Position) >= range)
            {
                Logger.LogVerbose("Moving to " + destinationName);
                PlayerMover.NavigateTo(location, destinationName);
                await Coroutine.Yield();
            }

            var distance = location.Distance(ZetaDia.Me.Position);
            if (distance <= range)
                Navigator.PlayerMover.MoveStop();

            Logger.LogVerbose("MoveTo Finished. Distance={0}", distance);
            return true;
        }

    }
}
