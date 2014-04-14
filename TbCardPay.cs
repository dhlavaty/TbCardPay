/*
 * TbCardPay - minimalist C# Tatrabanka CardPay implementation
 * ==================================================
 * version 0.1.0 (2014-04-10)  
 * (c) 2014 Dušan Hlavatý (dhlavaty@gmail.com)  
 * freely distributable under The MIT License (MIT)  
 * https://github.com/dhlavaty/TbCardPay
 * 
 */
using System;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DuDDo.Payments
{
    /// <summary>
    /// Tatrabanka CardPay AES-256 version - minimalist implementation (K.I.S.S. design principle)
    /// </summary>
    public class TbCardPay
    {
        /// <summary>
        /// AMT (float) povinny
        /// Suma, ktorú klient prevádza na obchodníkov účet. Desatinná časť je oddelená bodkou.
        /// 
        /// 9+2 znakov
        /// Max.2 desatinné miesta – oddelené vždy bodkou.
        /// </summary>
        public double AMT { get; private set; }

        /// <summary>
        /// MID (int) povinny
        /// Jedinečné identifikačné číslo obchodníka, ku ktorému je priradený účet obchodníka a bezpečnostný kľúč, určený na zabezpečenie správ.
        /// Nacitava sa z .config suboru (kluc "CardPay:Mid")
        /// </summary>
        public int MID
        {
            get
            {
                if (this._mid == Int32.MinValue)
                {
                    return Int32.Parse(ConfigurationManager.AppSettings["CardPay:Mid"]);
                }

                return this._mid;
            }

            protected set
            {
                this._mid = value;
            }
        }
        private int _mid = Int32.MinValue;

        /// <summary>
        /// PT (string) nepovinny
        /// Identifikátor služby - Môže obsahovať iba hodnotu „CardPay“
        /// </summary>
        public string PT { get { return "CardPay"; } }

        /// <summary>
        /// CURR (integer) povinny
        /// Mena v ktorej bude transakcia vykonaná. Vid <see cref="Currency"/> enum
        /// </summary>
        public Currency CURR { get; private set; }

        /// <summary>
        /// VS (string) povinny
        /// Variabilný symbol. Jednoznačný identifikátor platby. Môže obsahovať iba číslice 0-9.
        /// max. 10 cislic
        /// </summary>
        public int VS { get; private set; }

        /// <summary>
        /// RURL (string) povinny
        /// Návratová URL adresa na ktorú banka presmeruje klienta po vykonaní platby.
        /// URL musí byť vytvorená v súlade s RFC 1738 a adresa zadaná v RURL po presmerovaní musí byť funkčná.
        /// max. 256 znakov
        /// </summary>
        public string RURL { get; private set; }

        /// <summary>
        /// IPC (string) povinny
        /// IP adresa klienta. Ak nie je k dispozícii, tak IP adresa proxy servera.
        /// </summary>
        public string IPC { get; private set; }

        /// <summary>
        /// NAME (string) povinny
        /// Meno klienta z objednávkového formulára zo stránky obchodníka.
        /// Meno NESMIE obsahovať diakritiku. Povolené znaky: 0-9, a-z, A-Z, medzera, bodky, pomlčka, podčiarkovník, @
        /// max. 30 znakov
        /// </summary>
        public string NAME { get; private set; }

        protected static string ApplicationHexEncryptKey
        {
            get
            {
                return ConfigurationManager.AppSettings["CardPay:HexEncryptKey"];
            }
        }

        protected string HexEncryptKey
        {
            get
            {
                if (this._hexEncryptKey == null)
                {
                    return ApplicationHexEncryptKey;
                }

                return this._hexEncryptKey;
            }

            set
            {
                this._hexEncryptKey = value;
            }
        }
        private string _hexEncryptKey = null;

        /// <summary>
        /// SIGN (string) povinny
        /// Bezpečnostný podpis vygenerovaný na strane obchodníka. Môže obsahovať iba veľké písmená a čísla (A-Z, 0-9).
        /// 32 znakov
        /// </summary>
        public string SIGN
        {
            get
            {
                if (this.isSelfTestRunning == false)
                {
                    // poor mans unit test - run a selftest
                    TbCardPay.SelfTest();
                }

                // hash = SHA1( MID + AMT + CURR + VS + RURL + IPC + NAME )
                // SIGN = BYTE2HEX( AES256( hash[0..15], key ) )

                StringBuilder sb = new StringBuilder();
                sb.Append(this.MID.ToString(CultureInfo.InvariantCulture));
                sb.Append(String.Format(CultureInfo.InvariantCulture, "{0:0.00}", this.AMT));
                sb.Append(((int)this.CURR).ToString(CultureInfo.InvariantCulture));
                sb.Append(this.VS.ToString(CultureInfo.InvariantCulture));
                sb.Append(this.RURL);
                sb.Append(this.IPC);
                sb.Append(this.NAME);
                var toSign = sb.ToString();

                return CardPaySign(toSign, this.HexEncryptKey);
            }
        }

        /// <summary>
        /// URL adresa platobnej brany CardPay - nezabudni nastavit FORM 'enctype' na 'application/x-www-form-urlencoded')
        /// 
        /// Priklad:
        /// [form action="@FormActionUrl" enctype="application/x-www-form-urlencoded" method="post"]
        /// [/form]
        /// </summary>
        public string FormActionUrl
        {
            get
            {
                if (this._formActionUrl == null)
                {
                    return ConfigurationManager.AppSettings["CardPay:FormActionUrl"];
                }

                return this._formActionUrl;
            }

            protected set
            {
                this._formActionUrl = value;
            }
        }
        private string _formActionUrl = null;

        /// <summary>
        /// Variable to prevent recursion in SelfTesting.
        /// </summary>
        private bool isSelfTestRunning = false;

        /// <summary>
        /// Use <see cref="CreateRequest"/> to construct class instance
        /// </summary>
        private TbCardPay()
        {
        }

        /// <summary>
        /// Create CardPay request
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="currency"></param>
        /// <param name="varSymbol"></param>
        /// <param name="returnUrl"></param>
        /// <param name="ipAddress"></param>
        /// <param name="clientName">Meno NESMIE obsahovať diakritiku. Povolené znaky: 0-9, a-z, A-Z, medzera, bodky, pomlčka, podčiarkovník, @</param>
        /// <returns></returns>
        public static TbCardPay CreateRequest(double amount, Currency currency, int varSymbol, string returnUrl, string ipAddress, string clientName)
        {
            var ret = new TbCardPay();
            ret.AMT = amount;
            ret.CURR = currency;
            ret.VS = varSymbol;
            ret.RURL = returnUrl;
            ret.IPC = ipAddress;
            ret.NAME = clientName;

            return ret;
        }

        private static bool CheckBankResponse(int vs, string res, string ac, string sign, string applicationHexEncryptKey)
        {
            // VS={parameter VS}&RES={parameter RES}& AC={parameter AC}&SIGN={bezp. podpis}
            StringBuilder sb = new StringBuilder();
            sb.Append(vs.ToString(CultureInfo.InvariantCulture));
            sb.Append(res);
            sb.Append(ac ?? "");

            var calculatedSign = CardPaySign(sb.ToString(), applicationHexEncryptKey);

            if (calculatedSign == sign)
            {
                // Podpis je OK, plati vysledok RES a AC
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns <c>true</c> if bank response is correctly signed so you can process "vs", "res" and "ac" parameters;
        /// Now, if "res" contains "OK" you could accept payment.
        /// If "res" contains "FAIL" you should ignore payment - it was not processed by bank.
        /// </summary>
        /// <param name="vs"></param>
        /// <param name="res"></param>
        /// <param name="ac"></param>
        /// <param name="sign"></param>
        /// <returns><c>true</c> if bank response correctly signed so parameters "res" and "ac" are valid;</returns>
        public static bool CheckBankResponse(int vs, string res, string ac, string sign)
        {
            return CheckBankResponse(vs, res, ac, sign, TbCardPay.ApplicationHexEncryptKey);
        }

        /// <summary>
        /// Vrati HTML hidden polia potrebne pre formular.
        /// </summary>
        /// <returns></returns>
        public string HtmlHiddenFields()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("<input type='hidden' name='PT' value='{0}' />", this.PT);
            sb.AppendFormat("<input type='hidden' name='MID' value='{0}' />", this.MID.ToString(CultureInfo.InvariantCulture));
            sb.AppendFormat(CultureInfo.InvariantCulture, "<input type='hidden' name='AMT' value='{0:0.00}' />", this.AMT);
            sb.AppendFormat("<input type='hidden' name='CURR' value='{0}' />", ((int)this.CURR).ToString(CultureInfo.InvariantCulture));
            sb.AppendFormat("<input type='hidden' name='VS' value='{0}' />", this.VS.ToString(CultureInfo.InvariantCulture));
            sb.AppendFormat("<input type='hidden' name='RURL' value='{0}' />", this.RURL);
            sb.AppendFormat("<input type='hidden' name='IPC' value='{0}' />", this.IPC);
            sb.AppendFormat("<input type='hidden' name='NAME' value='{0}' />", this.NAME);
            sb.AppendFormat("<input type='hidden' name='SIGN' value='{0}' />", this.SIGN);

            return sb.ToString();
        }

        /// <summary>
        /// SelfTest to ensure that our hash/encryption algorithms are correct.
        /// Throws an Exception when selftest did not pass.
        /// </summary>
        /// <returns></returns>
        public static TbCardPay SelfTest()
        {
            TbCardPay request = TbCardPay.CreateRequest(1234.5, Currency.EUR, 1111, "https://moja.tatrabanka.sk/cgi-bin/e-commerce/start/example.jsp", "1.2.3.4", "JanPokusny");
            request.MID = 9999;
            request.HexEncryptKey = "1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D";
            request.FormActionUrl = "https://moja.tatrabanka.sk/cgi-bin/e-commerce/start/example.jsp";
            request.isSelfTestRunning = true;

            if (request.SIGN != "4E7DF35F91A19F6F6A4A0AF5534AC919")
            {
                throw new ApplicationException("TbCardPay self request test not passed");
            }

            if (TbCardPay.CheckBankResponse(1111, "OK", "123456", "781C110AD840077E470E1D5C9F944D7D", applicationHexEncryptKey: request.HexEncryptKey) == false)
            {
                throw new ApplicationException("TbCardPay self response test not passed");
            }

            // Self test passed OK
            return request;
        }

        #region Some helpers
        public static string ByteArrayToHexString(byte[] byteArray)
        {
            StringBuilder hex = new StringBuilder(byteArray.Length * 2);
            foreach (byte b in byteArray)
            {
                hex.AppendFormat("{0:X2}", b);
            }

            return hex.ToString();
        }

        /// <summary>
        /// AWARE: This will always returns 32 bytes array (padded with zero values) for AES-256 key 
        /// </summary>
        /// <param name="hex"></param>
        /// <returns></returns>
        public static byte[] HexStringTo32ByteArray(String hex)
        {
            int numOfChars = hex.Length;
            byte[] bytes = new byte[256 / 8];
            for (int i = 0; i < numOfChars; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }

            return bytes;
        }

        public static string CardPaySign(string toSign, string hexEncryptKey)
        {
            byte[] toHash = Encoding.ASCII.GetBytes(toSign);
            byte[] hash;
            using (SHA1 sha = new SHA1Managed())
            {
                hash = sha.ComputeHash(toHash);
            }

            byte[] toEncrypt = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                toEncrypt[i] = hash[i];
            }

            byte[] encrypted;
            using (AesManaged aes = new AesManaged())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.KeySize = 256;
                aes.Key = HexStringTo32ByteArray(hexEncryptKey);

                ICryptoTransform encryptor = aes.CreateEncryptor();

                // Create the streams used for encryption. 
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        csEncrypt.Write(toEncrypt, 0, 16);
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }

            return ByteArrayToHexString(encrypted);
        }

        public enum Currency : int
        {
            /// <summary>
            /// Euro = 978
            /// </summary>
            EUR = 978,

            /// <summary>
            /// Czech koruna = 203
            /// </summary>
            CZK = 203,

            /// <summary>
            /// US dollar = 840
            /// </summary>
            USD = 840,

            /// <summary>
            /// British Pound = 826
            /// </summary>
            GBP = 826,

            /// <summary>
            /// Hungarian Forint = 348
            /// </summary>
            HUF = 348,
            
            /// <summary>
            /// Polish Zloty = 985
            /// </summary>
            PLN = 985,
            
            /// <summary>
            /// Swiss Frank = 756
            /// </summary>
            CHF = 756,
            
            /// <summary>
            /// DKK = 208
            /// </summary>
            DKK = 208
        }
        #endregion Some helpers
    } // class
} // namespace