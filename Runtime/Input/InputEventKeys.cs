namespace LFramework
{
    /// <summary>
    /// 框架级输入事件键名。
    /// 由 InputKeysManager 派发，业务层可通过 EventCenter 订阅这些事件。
    /// 字符串值与原 EventConstKey 中的输入键完全一致，保证向后兼容。
    /// </summary>
    public static class InputEventKeys
    {
        public const string KeyDown = "GetKeyDown";
        public const string KeyUp = "GetKeyUp";
        public const string Key = "GetKey";

        public const string MouseButtonDown = "GetMouseButtonDown";
        public const string MouseButtonUp = "GetMouseButtonUp";
        public const string MouseButton = "GetMouseButton";
        public const string MousePosition = "GetMousePosition";
        public const string MouseScrollDelta = "GetMouseScrollDelta";
        public const string MouseDelta = "GetMouseDelta";
        public const string Mouse = "GetMouse";

        public const string Touch = "GetTouch";
        public const string TouchCount = "GetTouchCount";
        public const string TouchPosition = "GetTouchPosition";
    }
}
