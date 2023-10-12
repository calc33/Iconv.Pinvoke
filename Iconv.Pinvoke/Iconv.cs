using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text.Unicode;
using System.Threading;
using System.Xml;
using static System.Net.Mime.MediaTypeNames;

namespace LibIconv
{
    public enum ConvertionOption
    {
        None,
        Transliteration,
        Ignore,
    }

    public partial class Iconv : IDisposable
    {
        private static readonly string[] ConvertionOptionStr = new string[] { string.Empty, "//TRANSLIT", "//IGNORE" };

        private IntPtr _handle;
        private static readonly IntPtr INVALID_ICONV_HANDLE = new IntPtr(-1);

        public Iconv(string tocode, string fromcode, ConvertionOption convertionOption)
        {
            string opt = (0 <= (int)convertionOption && (int)convertionOption <= ConvertionOptionStr.Length) ? ConvertionOptionStr[(int)convertionOption] : string.Empty;
            _handle = NativeMethods.libiconv_open(tocode + opt, fromcode);
            if (_handle == INVALID_ICONV_HANDLE)
            {
                throw new ArgumentException();
            }
        }

        private const int E2BIG = 7;
        private const int EILSEQ = 84;
        private const int EINVAL = 22;

        private const int BUFFER_SIZE_W = 4096 * 2;
        private const int BUFFER_SIZE_R = 4096;
        public void Execute(Stream streamWrite, Stream streamRead)
        {
            IntPtr ptrW0 = Marshal.AllocHGlobal(BUFFER_SIZE_W);
            IntPtr ptrR0 = Marshal.AllocHGlobal(BUFFER_SIZE_R);
            try
            {
                byte[] bufW = new byte[BUFFER_SIZE_W];
                byte[] bufR = new byte[BUFFER_SIZE_R];
                int posR = 0;
                for (int n = streamRead.Read(bufR, posR, BUFFER_SIZE_R - posR); 0 < n; n = streamRead.Read(bufR, posR, BUFFER_SIZE_R - posR))
                {
                    long leftR = posR + n;
                    long leftW = BUFFER_SIZE_W;
                    Marshal.Copy(bufR, 0, ptrR0, (int)leftR);
                    IntPtr ptrW = ptrW0;
                    IntPtr ptrR = ptrR0;
                    long ret = NativeMethods.libiconv(_handle, ref ptrR, ref leftR, ref ptrW, ref leftW);
                    if (ret == -1)
                    {
                        int errno = NativeMethods.errno;
                        switch (errno)
                        {
                            case EINVAL:
                                throw new ApplicationException("An incomplete multibyte sequence has been encountered in the input.");
                            case E2BIG:
                                throw new ApplicationException("There is not sufficient room at outbuf.");
                            case EILSEQ:
                                throw new ApplicationException("An invalid multibyte sequence has been encountered in the input.");
                            default:
                                throw new ApplicationException(string.Format("Unknown errno: {0}", errno));
                        }
                    }
                    int nW = (ptrW - ptrW0).ToInt32();
                    Marshal.Copy(ptrW0, bufW, 0, nW);
                    streamWrite.Write(bufW, 0, nW);

                    // bufR中に未処理のデータがあれば次回処理のために前に詰める
                    int nR = (ptrR - ptrR0).ToInt32();
                    for (int i = nR; i < n; i++)
                    {
                        bufR[i - nR] = bufR[i];
                    }
                    posR = n - nR;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptrW0);
                Marshal.FreeHGlobal(ptrR0);
            }
        }

        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (_handle != IntPtr.Zero)
            {
                if (disposing)
                {
                    // TODO: マネージド状態を破棄します (マネージド オブジェクト)
                }

                NativeMethods.libiconv_close(_handle);
                // TODO: アンマネージド リソース (アンマネージド オブジェクト) を解放し、ファイナライザーをオーバーライドします
                // TODO: 大きなフィールドを null に設定します
                _handle = IntPtr.Zero;
            }
        }

        // // TODO: 'Dispose(bool disposing)' にアンマネージド リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします
        ~Iconv()
        {
            // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion

        private static class NativeMethods
        {
            [DllImport("libiconv.dll", CharSet = CharSet.Ansi)]
            public static extern IntPtr libiconv_open([MarshalAs(UnmanagedType.LPStr)] string tocode, [MarshalAs(UnmanagedType.LPStr)] string fromcode);
            [DllImport("libiconv.dll")]
            public static extern int libiconv_close(IntPtr cd);

            [DllImport("libiconv.dll")]
            public static extern long libiconv(IntPtr cd, ref IntPtr inbuf, ref long inbtyesleft, ref IntPtr outbuf, ref long outbytesleft);

            public static int errno
            {
                get
                {
                    return Marshal.ReadInt32(_errno());
                }
            }

            [DllImport("api-ms-win-crt-runtime-l1-1-0.dll")]
            public static extern IntPtr _errno();
        }
    }
}