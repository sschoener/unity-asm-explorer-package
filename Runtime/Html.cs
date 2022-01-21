using System.Text;

namespace AsmExplorer
{
    public static class Html {
        public static string Url(string target, params string[] arguments) {
            StringBuilder sb = new StringBuilder(target.Length);
            sb.Append(target);
            for (int i = 0; i < arguments.Length; i += 2) {
                sb.Append(i == 0 ? '?' : '&');
                sb.Append(arguments[i]);
                if (arguments[i+1] != null && arguments[i+1].Length > 0) {
                    sb.Append('=');
                    sb.Append(System.Uri.EscapeDataString(arguments[i+1]));
                }
            }
            return sb.ToString();
        }
    }
}