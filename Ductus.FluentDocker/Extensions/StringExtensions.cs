
namespace Ductus.FluentDocker.Extensions {
    public static class StringExtensions {

        /// <summary>
        /// This function will wrap the string s with string c (start and end) if
        /// not already existant. If already exist, it will leave it, hence it
        /// do not double wrap.
        /// </summary>
        /// <param name="s">The string to wrap.</param>
        /// <param name="c">The string to check and wrap with if not existing.</param>
        /// <returns>The wrapped string.</returns>
        public static string WrapWithChar(this string s, string c) {

            if (!s.StartsWith(c)) {
                s = c + s;
            }

            if (!s.EndsWith(c)) {
                s = s + c;
            }

            return s;
        }   
    }
}