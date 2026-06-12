namespace GameHelper.Plugin
{
    /// <summary>
    ///     Container for plugin metadata.
    /// </summary>
    internal class PluginMetadata
    {
        /// <summary>
        ///     Gets or sets a value indicating whether the plugin is active this session.
        /// </summary>
        public bool Enable { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether the plugin loads automatically on startup.
        /// </summary>
        public bool AutoStart { get; set; }
    }
}
