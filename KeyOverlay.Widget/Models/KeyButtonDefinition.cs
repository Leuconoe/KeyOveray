using System;
using Windows.System;

namespace KeyOverlay.Widget.Models
{
    public sealed class KeyButtonDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public int VirtualKey { get; set; }
        public VirtualKeyModifiers Modifiers { get; set; }
        public string DisplayName { get; set; }

        public static KeyButtonDefinition Create(int virtualKey, string displayName, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None)
        {
            return new KeyButtonDefinition
            {
                VirtualKey = virtualKey,
                DisplayName = displayName,
                Modifiers = modifiers
            };
        }
    }
}
