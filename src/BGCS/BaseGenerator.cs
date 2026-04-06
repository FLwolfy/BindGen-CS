namespace BGCS
{
    using BGCS.Core;

    /// <summary>
    /// Declares the callback signature <c>callback</c>.
    /// </summary>
    public delegate void GenEventHandler<TSender, TArgs>(TSender sender, TArgs args);

    /// <summary>
    /// Defines the public class <c>BaseGenerator</c>.
    /// </summary>
    public class BaseGenerator : LoggerBase
    {
        protected CsCodeGeneratorConfig config;

        /// <summary>
        /// Initializes a new instance of <see cref="BaseGenerator"/>.
        /// </summary>
        public BaseGenerator(CsCodeGeneratorConfig config)
        {
            this.config = config;
        }

        /// <summary>
        /// Exposes public member <c>config</c>.
        /// </summary>
        public CsCodeGeneratorConfig Settings => config;

        /// <summary>
        /// Raises notifications for this component.
        /// </summary>
        public event GenEventHandler<CsCodeGenerator, CsCodeGeneratorConfig>? PostConfigure;

        protected virtual void OnPostConfigure(CsCodeGeneratorConfig config)
        {
            PostConfigure?.Invoke((CsCodeGenerator)this, config);
        }
    }
}
