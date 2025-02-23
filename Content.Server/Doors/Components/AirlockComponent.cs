using System.Threading;
using Content.Server.Power.Components;
// using Content.Server.WireHacking;
using Content.Shared.Doors.Components;
using Content.Shared.Sound;
using Robust.Shared.Audio;
using Robust.Shared.Player;
// using static Content.Shared.Wires.SharedWiresComponent;
// using static Content.Shared.Wires.SharedWiresComponent.WiresAction;

namespace Content.Server.Doors.Components
{
    /// <summary>
    /// Companion component to DoorComponent that handles airlock-specific behavior -- wires, requiring power to operate, bolts, and allowing automatic closing.
    /// </summary>
    [RegisterComponent]
    [ComponentReference(typeof(SharedAirlockComponent))]
    public sealed class AirlockComponent : SharedAirlockComponent
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;

        /// <summary>
        /// Sound to play when the bolts on the airlock go up.
        /// </summary>
        [DataField("boltUpSound")]
        public SoundSpecifier BoltUpSound = new SoundPathSpecifier("/Audio/Machines/boltsup.ogg");

        /// <summary>
        /// Sound to play when the bolts on the airlock go down.
        /// </summary>
        [DataField("boltDownSound")]
        public SoundSpecifier BoltDownSound = new SoundPathSpecifier("/Audio/Machines/boltsdown.ogg");

        /// <summary>
        /// Duration for which power will be disabled after pulsing either power wire.
        /// </summary>
        [DataField("powerWiresTimeout")]
        public float PowerWiresTimeout = 5.0f;

        /// <summary>
        /// Whether the maintenance panel should be visible even if the airlock is opened.
        /// </summary>
        [DataField("openPanelVisible")]
        public bool OpenPanelVisible = false;

        private CancellationTokenSource _powerWiresPulsedTimerCancel = new();
        private bool _powerWiresPulsed;

        /// <summary>
        /// True if either power wire was pulsed in the last <see cref="PowerWiresTimeout"/>.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        private bool PowerWiresPulsed
        {
            get => _powerWiresPulsed;
            set
            {
                _powerWiresPulsed = value;
                // UpdateWiresStatus();
                // UpdatePowerCutStatus();
            }
        }

        private bool _boltsDown;

        [ViewVariables(VVAccess.ReadWrite)]
        public bool BoltsDown
        {
            get => _boltsDown;
            set
            {
                _boltsDown = value;
                UpdateBoltLightStatus();
            }
        }

        private bool _boltLightsEnabled = true;

        public bool BoltLightsEnabled
        {
            get => _boltLightsEnabled;
            set
            {
                _boltLightsEnabled = value;
                UpdateBoltLightStatus();
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        public bool BoltLightsVisible
        {
            get => _boltLightsEnabled && BoltsDown && IsPowered()
                && _entityManager.TryGetComponent<DoorComponent>(Owner, out var doorComponent) && doorComponent.State == DoorState.Closed;
            set
            {
                _boltLightsEnabled = value;
                UpdateBoltLightStatus();
            }
        }

        /// <summary>
        /// Delay until an open door automatically closes.
        /// </summary>
        [DataField("autoCloseDelay")]
        public TimeSpan AutoCloseDelay = TimeSpan.FromSeconds(5f);

        /// <summary>
        /// Multiplicative modifier for the auto-close delay. Can be modified by hacking the airlock wires. Setting to
        /// zero will disable auto-closing.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        public float AutoCloseDelayModifier = 1.0f;

        protected override void Initialize()
        {
            base.Initialize();

            if (_entityManager.TryGetComponent<ApcPowerReceiverComponent>(Owner, out var receiverComponent) &&
                _entityManager.TryGetComponent<AppearanceComponent>(Owner, out var appearanceComponent))
            {
                appearanceComponent.SetData(DoorVisuals.Powered, receiverComponent.Powered);
            }
        }

        public bool CanChangeState()
        {
            return IsPowered() && !IsBolted();
        }

        public bool IsBolted()
        {
            return _boltsDown;
        }

        public bool IsPowered()
        {
            return !_entityManager.TryGetComponent<ApcPowerReceiverComponent>(Owner, out var receiverComponent) || receiverComponent.Powered;
        }

        public void UpdateBoltLightStatus()
        {
            if (_entityManager.TryGetComponent<AppearanceComponent>(Owner, out var appearanceComponent))
            {
                appearanceComponent.SetData(DoorVisuals.BoltLights, BoltLightsVisible);
            }
        }

        public void SetBoltsWithAudio(bool newBolts)
        {
            if (newBolts == BoltsDown)
            {
                return;
            }

            BoltsDown = newBolts;

            SoundSystem.Play(Filter.Broadcast(), newBolts ? BoltDownSound.GetSound() : BoltUpSound.GetSound(), Owner);
        }
    }
}
