﻿using System;
using System.Threading.Tasks;
using Adventurer.Game.Actors;
using Adventurer.Util;
using Buddy.Coroutines;
using Zeta.Game;
using Zeta.Game.Internals;
using Zeta.Game.Internals.Actors;
using Zeta.Game.Internals.SNO;

namespace Adventurer.Coroutines
{
    public sealed class InteractionCoroutine
    {

        #region State

        public enum States
        {
            NotStarted,
            Checking,
            Interacting,
            TimedOut,
            Completed,
            Failed
        }

        private States _state;
        public States State
        {
            get { return _state; }
            private set
            {
                if (_state == value) return;
                if (value != States.NotStarted)
                {
                    Logger.Debug("[Interaction] " + value);
                }
                _state = value;
            }
        }

        #endregion

        private readonly int _actorId;
        private readonly TimeSpan _timeOut;
        private readonly TimeSpan _sleepTime;
        private readonly int _interactAttempts;
        private int _currentInteractAttempt = 1;
        private DateTime _interactionStartedAt;
        private bool _timeoutCheckEnabled;
        private bool _isPortal;
        private bool _isNephalemStone;
        private bool _isOrek;

        public InteractionCoroutine(int actorId, TimeSpan timeOut, TimeSpan sleepTime, int interactAttempts = 1)
        {
            _actorId = actorId;
            _timeOut = timeOut;
            _sleepTime = sleepTime;
            _interactAttempts = interactAttempts;
            if (_timeOut != default(TimeSpan))
            {
                _timeoutCheckEnabled = true;
            }
            if (_sleepTime == default(TimeSpan))
            {
                _sleepTime = new TimeSpan(0, 0, 1);
            }
        }


        public async Task<bool> GetCoroutine()
        {
            switch (State)
            {
                case States.NotStarted:
                    return NotStarted();
                case States.Checking:
                    return Checking();
                case States.Interacting:
                    return await Interacting();
                case States.TimedOut:
                    return TimedOut();
                case States.Completed:
                    return Completed();
                case States.Failed:
                    return Failed();
            }
            return false;
        }


        private bool NotStarted()
        {
            State = States.Checking;
            return false;
        }

        private bool Checking()
        {
            var actor = ActorFinder.FindObject(_actorId);
            if (actor == null)
            {
                Logger.Debug("[Interaction] Nothing to interact, failing. ");
                State = States.Failed;
                return false;
            }
            if (!actor.IsInteractableQuestObject())
            {
                Logger.Debug("[Interaction] The object is not valid or not interactable, failing.");
                State = States.Failed;
                return false;
            }
            if (actor is DiaGizmo)
            {
                var gizmoActor = (DiaGizmo)actor;
                if (gizmoActor.IsDestructibleObject)
                {

                }
            }

            if (actor is DiaGizmo && (actor as DiaGizmo).IsPortal)
            {
                _isPortal = true;
            }
            if (actor.ActorSNO == 364715)
            {
                _isNephalemStone = true;
            }
            if (actor.ActorSNO == 363744)
            {
                _isOrek = true;
            }
            if (_isNephalemStone && UIElements.RiftDialog.IsVisible)
            {
                State=States.Completed;
                return false;
            }
            if (_isOrek && AdvDia.RiftQuest.State == QuestState.Completed)
            {
                State = States.Completed;
                return false;
            }
            State = States.Interacting;
            return false;
        }

        private async Task<bool> Interacting()
        {
            if (ZetaDia.Me.IsFullyValid() && (ZetaDia.Me.CommonData.AnimationState == AnimationState.Casting || ZetaDia.Me.CommonData.AnimationState == AnimationState.Channeling))
            {
                Logger.Debug("[Interaction] Waiting for the cast to end");
                await Coroutine.Sleep(500);
                return false;
            }

            var actor = ActorFinder.FindObject(_actorId);
            if (actor == null)
            {
                Logger.Debug("[Interaction] Nothing to interact, failing. ");
                State = States.Failed;
                return false;
            }
            // Assume done
            if (!actor.IsInteractableQuestObject())
            {
                State=States.Completed;
                return false;
            }
            if (_currentInteractAttempt > _interactAttempts)
            {
                State = States.Completed;
                return true;
            }

            if (_timeoutCheckEnabled)
            {
                if (_interactionStartedAt == default(DateTime))
                {
                    _interactionStartedAt = DateTime.UtcNow;
                }
                else
                {
                    if (DateTime.UtcNow - _interactionStartedAt > _timeOut)
                    {
                        Logger.Debug("[Interaction] Interaction timed out after {0} seconds", (DateTime.UtcNow - _interactionStartedAt).TotalSeconds);
                        State = States.TimedOut;
                        return false;
                    }
                }
            }

            if (await Interact(actor))
            {
                if (_currentInteractAttempt < _interactAttempts)
                {
                    _currentInteractAttempt++;
                    return false;
                }
                if (!_isPortal && !_isNephalemStone && !_isOrek && actor.IsInteractableQuestObject())
                {
                    return false;
                }
                if (_isNephalemStone && !UIElements.RiftDialog.IsVisible)
                {
                    return false;
                }
                if (_isOrek && AdvDia.RiftQuest.State != QuestState.Completed)
                {
                    return false;
                }
                State = States.Completed;
            }
            return false;
        }

        private static bool TimedOut()
        {
            return true;
        }

        private static bool Completed()
        {
            return true;
        }

        private static bool Failed()
        {
            return true;
        }

        private async Task<bool> Interact(DiaObject actor)
        {
            Logger.Debug("[Interaction] Attempting to interact with {0} at distance {1}", ((SNOActor)actor.ActorSNO).ToString(), actor.Distance);
            bool retVal = false;
            switch (actor.ActorType)
            {
                case ActorType.Gizmo:
                    switch (actor.ActorInfo.GizmoType)
                    {
                        case GizmoType.BossPortal:
                        case GizmoType.Portal:
                        case GizmoType.ReturnPortal:
                            retVal = ZetaDia.Me.UsePower(SNOPower.GizmoOperatePortalWithAnimation, actor.Position);
                            break;
                        default:
                            retVal = ZetaDia.Me.UsePower(SNOPower.Axe_Operate_Gizmo, actor.Position);
                            break;
                    }
                    break;
                case ActorType.Monster:
                    retVal = ZetaDia.Me.UsePower(SNOPower.Axe_Operate_NPC, actor.Position);
                    break;
            }

            // Doubly-make sure we interact
            actor.Interact();
            await Coroutine.Sleep(_sleepTime);
            return retVal;
        }


        public void Reset()
        {
            _interactionStartedAt = default(DateTime);
            _timeoutCheckEnabled = false;
            State = States.NotStarted;
        }
    }
}
