using System.Diagnostics.CodeAnalysis;
using Content.Shared.Alert;
using Content.Shared.GameTicking;
using Content.Shared.Input;
using Content.Shared.Movement.Components;
using Content.Shared.Physics.Pull;
using Content.Shared.Pulling.Components;
using Content.Shared.Verbs;
using JetBrains.Annotations;
using Robust.Shared.Containers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Players;

namespace Content.Shared.Pulling
{
    [UsedImplicitly]
    public abstract partial class SharedPullingSystem : EntitySystem
    {
        [Dependency] private readonly SharedPullingStateManagementSystem _pullSm = default!;
        [Dependency] private readonly AlertsSystem _alertsSystem = default!;

        /// <summary>
        ///     A mapping of pullers to the entity that they are pulling.
        /// </summary>
        private readonly Dictionary<EntityUid, EntityUid> _pullers =
            new();

        private readonly HashSet<SharedPullableComponent> _moving = new();
        private readonly HashSet<SharedPullableComponent> _stoppedMoving = new();

        public IReadOnlySet<SharedPullableComponent> Moving => _moving;

        public override void Initialize()
        {
            base.Initialize();

            UpdatesOutsidePrediction = true;

            SubscribeLocalEvent<RoundRestartCleanupEvent>(Reset);
            SubscribeLocalEvent<PullStartedMessage>(OnPullStarted);
            SubscribeLocalEvent<PullStoppedMessage>(OnPullStopped);
            SubscribeLocalEvent<EntInsertedIntoContainerMessage>(HandleContainerInsert);

            SubscribeLocalEvent<SharedPullableComponent, PullStartedMessage>(PullableHandlePullStarted);
            SubscribeLocalEvent<SharedPullableComponent, PullStoppedMessage>(PullableHandlePullStopped);

            SubscribeLocalEvent<SharedPullableComponent, GetVerbsEvent<Verb>>(AddPullVerbs);

            CommandBinds.Builder
                .Bind(ContentKeyFunctions.MovePulledObject, new PointerInputCmdHandler(HandleMovePulledObject))
                .Register<SharedPullingSystem>();
        }

        private void AddPullVerbs(EntityUid uid, SharedPullableComponent component, GetVerbsEvent<Verb> args)
        {
            if (args.Hands == null || !args.CanAccess || !args.CanInteract)
                return;

            // Are they trying to pull themselves up by their bootstraps?
            if (args.User == args.Target)
                return;

            //TODO VERB ICONS add pulling icon
            if (component.Puller == args.User)
            {
                Verb verb = new();
                verb.Text = Loc.GetString("pulling-verb-get-data-text-stop-pulling");
                verb.Act = () => TryStopPull(component, args.User);
                args.Verbs.Add(verb);
            }
            else if (CanPull(args.User, args.Target))
            {
                Verb verb = new();
                verb.Text = Loc.GetString("pulling-verb-get-data-text");
                verb.Act = () => TryStartPull(args.User, args.Target);
                args.Verbs.Add(verb);
            }
        }

        // Raise a "you are being pulled" alert if the pulled entity has alerts.
        private void PullableHandlePullStarted(EntityUid uid, SharedPullableComponent component, PullStartedMessage args)
        {
            if (args.Pulled.Owner != uid)
                return;

            _alertsSystem.ShowAlert(component.Owner, AlertType.Pulled);
        }

        private  void PullableHandlePullStopped(EntityUid uid, SharedPullableComponent component, PullStoppedMessage args)
        {
            if (args.Pulled.Owner != uid)
                return;

            _alertsSystem.ClearAlert(component.Owner, AlertType.Pulled);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            _moving.ExceptWith(_stoppedMoving);
            _stoppedMoving.Clear();
        }

        public void Reset(RoundRestartCleanupEvent ev)
        {
            _pullers.Clear();
            _moving.Clear();
            _stoppedMoving.Clear();
        }

        private void OnPullStarted(PullStartedMessage message)
        {
            SetPuller(message.Puller.Owner, message.Pulled.Owner);
        }

        private void OnPullStopped(PullStoppedMessage message)
        {
            RemovePuller(message.Puller.Owner);
        }

        protected void OnPullableMove(EntityUid uid, SharedPullableComponent component, PullableMoveMessage args)
        {
            _moving.Add(component);
        }

        protected void OnPullableStopMove(EntityUid uid, SharedPullableComponent component, PullableStopMovingMessage args)
        {
            _stoppedMoving.Add(component);
        }

        // TODO: When Joint networking is less shitcodey fix this to use a dedicated joints message.
        private void HandleContainerInsert(EntInsertedIntoContainerMessage message)
        {
            if (EntityManager.TryGetComponent(message.Entity, out SharedPullableComponent? pullable))
            {
                TryStopPull(pullable);
            }

            if (EntityManager.TryGetComponent(message.Entity, out SharedPullerComponent? puller))
            {
                if (puller.Pulling == null) return;

                if (!EntityManager.TryGetComponent(puller.Pulling.Value, out SharedPullableComponent? pulling))
                {
                    return;
                }

                TryStopPull(pulling);
            }
        }

        private bool HandleMovePulledObject(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            if (session?.AttachedEntity is not { } player ||
                !player.IsValid())
                return false;

            if (!TryGetPulled(player, out var pulled))
            {
                return false;
            }

            if (!EntityManager.TryGetComponent(pulled.Value, out SharedPullableComponent? pullable))
            {
                return false;
            }

            if (_containerSystem.IsEntityInContainer(player) ||
                player.IsWeightless(entityManager: EntityManager))
                return false;

            TryMoveTo(pullable, coords);

            return false;
        }

        private void SetPuller(EntityUid puller, EntityUid pulled)
        {
            _pullers[puller] = pulled;
        }

        private bool RemovePuller(EntityUid puller)
        {
            return _pullers.Remove(puller);
        }

        public EntityUid GetPulled(EntityUid by)
        {
            return _pullers.GetValueOrDefault(by);
        }

        public bool TryGetPulled(EntityUid by, [NotNullWhen(true)] out EntityUid? pulled)
        {
            return (pulled = GetPulled(by)) != null;
        }

        public bool IsPulling(EntityUid puller)
        {
            return _pullers.ContainsKey(puller);
        }
    }
}
