namespace BGCS.Patching
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Captures regular-expression matches and rewrites candidate text from the last match to the first.
    /// </summary>
    public abstract class RegexPatch
    {
        /// <summary>
        /// Pattern used to capture matches before applying source rewrites.
        /// </summary>
        protected readonly Regex regex;
        private readonly string? m_targetFile;
        /// <summary>
        /// Compiles a pattern and limits the transformation to an optional candidate file.
        /// </summary>
        /// <param name="pattern">The regular-expression pattern used for each candidate text.</param>
        /// <param name="options">Matching options; Compiled is added when absent.</param>
        /// <param name="targetFile">An exact relative candidate file name, or null to process all files.</param>
        /// <exception cref="ArgumentException">The pattern is invalid.</exception>
        /// <exception cref="ArgumentNullException">The pattern is null.</exception>
        protected RegexPatch(
            string pattern,
            RegexOptions options,
            string? targetFile = null
        ) {
            if ((options & RegexOptions.Compiled) == 0)
            {
                options |= RegexOptions.Compiled;
            }

            this.regex = new Regex(pattern, options);
            this.m_targetFile = targetFile;
        }

        /// <summary>
        /// Compiles a pattern with default matching options and an optional exact candidate file filter.
        /// </summary>
        /// <param name="pattern">The regular-expression pattern used for each candidate text.</param>
        /// <param name="targetFile">An exact relative candidate file name, or null to process all files.</param>
        /// <exception cref="ArgumentException">The pattern is invalid.</exception>
        /// <exception cref="ArgumentNullException">The pattern is null.</exception>
        protected RegexPatch(
            string pattern,
            string? targetFile = null
        ) {
            this.regex = new Regex(pattern, RegexOptions.Compiled);
            this.m_targetFile = targetFile;
        }

        /// <summary>
        /// Borrows a prepared regular expression and an optional exact candidate file filter.
        /// </summary>
        /// <param name="regex">The expression retained for this transformation's lifetime.</param>
        /// <param name="targetFile">An exact relative candidate file name, or null to process all files.</param>
        /// <exception cref="ArgumentNullException">The expression is null.</exception>
        protected RegexPatch(
            Regex regex,
            string? targetFile = null
        ) {
            this.regex = regex ?? throw new ArgumentNullException(nameof(regex));
            this.m_targetFile = targetFile;
        }

        /// <summary>
        /// Rewrites matching candidate text while preserving offsets for earlier matches.
        /// </summary>
        /// <param name="file">The relative candidate file name used by the optional exact filter.</param>
        /// <param name="text">The complete mutable candidate text; unrelated files remain unchanged.</param>
        /// <exception cref="RegexMatchTimeoutException">The prepared expression exceeds its matching timeout.</exception>
        public virtual void PostPatch(
            string file,
            ref string text
        ) {
            if (this.m_targetFile != null && file != this.m_targetFile)
            {
                return;
            }

            var matches = this.regex.Matches(text);
            for (int index = matches.Count - 1; index >= 0; index--)
            {
                PostPatchMatch(file, ref text, matches[index]);
            }
        }

        /// <summary>
        /// Transforms one match captured from a staged generated source.
        /// </summary>
        /// <param name="file">Candidate source path.</param>
        /// <param name="text">Complete candidate text to replace; match offsets refer to the original pass input.</param>
        /// <param name="match">Match captured before any replacement in this pass.</param>
        protected abstract void PostPatchMatch(
            string file,
            ref string text,
            Match match
        );
    }
}
