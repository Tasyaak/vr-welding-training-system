using System;

namespace WeldingTrainer.Application
{
    /// Retains safety/command edges until capture and prevents held-trigger restart across contexts.
    public sealed class InputContextGate
    {
        private readonly float _press, _release;
        private InputCommandEdges _edges;
        private bool _pressed, _requireRelease = true;

        public InputContext Context { get; private set; } = InputContext.Suspended;
        public bool ProcessPressed => Context == InputContext.Training && !_requireRelease && _pressed;
        public bool RequiresRelease => _requireRelease;

        public InputContextGate(float pressThreshold, float releaseThreshold)
        {
            if (releaseThreshold < 0 || pressThreshold > 1 || releaseThreshold >= pressThreshold)
                throw new ArgumentOutOfRangeException(nameof(releaseThreshold));
            _press = pressThreshold; _release = releaseThreshold;
        }

        public void AdvanceTrigger(float value)
        {
            if (!_pressed && !_requireRelease && value >= _press)
            { _pressed = true; _edges |= InputCommandEdges.TriggerPressed; }
            else if ((_pressed || _requireRelease) && value <= _release)
            { if (_pressed) _edges |= InputCommandEdges.TriggerReleased; _pressed = false; _requireRelease = false; }
        }

        public void Queue(InputCommandEdges edge) => _edges |= edge;

        public void SetContext(InputContext context)
        {
            if (Context == context) return;
            Context = context; _pressed = false; _requireRelease = true;
        }

        public void Invalidate()
        { Context = InputContext.Suspended; _pressed = false; _requireRelease = true; }

        public InputCommandEdges ConsumeEdges()
        { InputCommandEdges result = _edges; _edges = InputCommandEdges.None; return result; }
    }
}
