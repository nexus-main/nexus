using SkiaSharp;
using System.Reflection;

namespace Nexus.UI.Services
{
    // https://github.com/mono/SkiaSharp/issues/1902
    // https://fontsgeek.com/fonts/Courier-New-Regular

    public class TypeFaceService
    {
        private readonly Dictionary<string, SKTypeface> _typeFaces = new();

        public SKTypeface GetTTF(string ttfName)
        {
            if (_typeFaces.ContainsKey(ttfName))
                return _typeFaces[ttfName];

            else if (LoadTypeFace(ttfName))
                return _typeFaces[ttfName];

            return SKTypeface.Default;
        }

        private bool LoadTypeFace(string ttfName)
        {
            var assembly = Assembly.GetExecutingAssembly();

            try
            {
                var fileName = ttfName.ToLower() + ".ttf";
                foreach (var item in assembly.GetManifestResourceNames())
                {
                    if (item.ToLower().EndsWith(fileName))
                    {
                        var stream = assembly.GetManifestResourceStream(item);
                        var typeFace = SKTypeface.FromStream(stream);

                        _typeFaces.Add(ttfName, typeFace);

                        return true;
                    }
                }
            }
            catch
            {
                /* missing resource */
            }

            return false;
        }
    }
}