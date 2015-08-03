/*
 * TbCardPay - minimalist C# Tatrabanka CardPay implementation (HMAC SHA256)
 * ==================================================
 * version 0.2.0 (2015-08-03)  
 * (c) 2014, 2015 Dušan Hlavatý (dhlavaty@gmail.com)  
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

        public string TIMESTAMP
        {
            get
            {
                if (String.IsNullOrWhiteSpace(this._timestamp))
                {
                    return DateTime.UtcNow.ToString("ddMMyyyyHHmmss");
                }

                return this._timestamp;
            }
            set
            {
                this._timestamp = value;
            }
        }
        private string _timestamp = null;

        /// <summary>
        /// HMAC (string) povinny
        /// Bezpečnostný podpis vygenerovaný na strane obchodníka. Môže obsahovať iba veľké písmená a čísla (A-Z, 0-9).
        /// 32 znakov
        /// </summary>
        public string HMAC
        {
            get
            {
                if (this.isSelfTestRunning == false)
                {
                    // poor mans unit test - run a selftest
                    TbCardPay.SelfTest();
                }

                // SIGN = BYTE2HEX( HMACSHA256( MID + AMT + CURR + VS + RURL + IPC + NAME + TIMESTAMP ) )

                StringBuilder sb = new StringBuilder();
                sb.Append(this.MID.ToString(CultureInfo.InvariantCulture));
                sb.Append(String.Format(CultureInfo.InvariantCulture, "{0:0.00}", this.AMT));
                sb.Append(((int)this.CURR).ToString(CultureInfo.InvariantCulture));
                sb.Append(this.VS.ToString(CultureInfo.InvariantCulture));
                sb.Append(this.RURL);
                sb.Append(this.IPC);
                sb.Append(this.NAME);
                // REM - (nepovinny)(max.50znakov) Emailová adresa pre zaslanie notifikácie o výsledku platby
                sb.Append(this.TIMESTAMP);
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

        private static bool CheckBankResponse(string amt, string curr, int vs, string res, string ac, string tid, string timestamp, string hmac, string applicationHexEncryptKey)
        {
            // AMT={parameter amt}&...&VS={parameter VS}&RES={parameter RES}&AC={parameter AC}&HMAC={bezp. podpis}
            // BYTE2HEX( HMACSHA256( AMT + CURR + VS + RES + AC + TID + TIMESTAMP ) )
            StringBuilder sb = new StringBuilder();
            sb.Append(amt);
            sb.Append(curr);
            sb.Append(vs.ToString(CultureInfo.InvariantCulture));
            sb.Append(res);
            sb.Append(ac ?? "");
            sb.Append(tid);
            sb.Append(timestamp);
            
            var calculatedHmac = CardPaySign(sb.ToString(), applicationHexEncryptKey);

            if (calculatedHmac == hmac)
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
        /// <param name="amt"></param>
        /// <param name="curr"></param>
        /// <param name="vs"></param>
        /// <param name="res"></param>
        /// <param name="ac"></param>
        /// <param name="tid"></param>
        /// <param name="timestamp"></param>
        /// <param name="hmac"></param>
        /// <returns><c>true</c> if bank response correctly signed so parameters "res" and "ac" are valid;</returns>
        public static bool CheckBankResponse(string amt, string curr, int vs, string res, string ac, string tid, string timestamp, string hmac)
        {
            return CheckBankResponse(amt, curr, vs, res, ac, tid, timestamp, hmac, TbCardPay.ApplicationHexEncryptKey);
        }

        /// <summary>
        /// Vrati HTML hidden polia potrebne pre formular.
        /// </summary>
        /// <returns></returns>
        public string HtmlHiddenFields()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("<input type='hidden' name='MID' value='{0}' />", this.MID.ToString(CultureInfo.InvariantCulture));
            sb.AppendFormat(CultureInfo.InvariantCulture, "<input type='hidden' name='AMT' value='{0:0.00}' />", this.AMT);
            sb.AppendFormat("<input type='hidden' name='CURR' value='{0}' />", ((int)this.CURR).ToString(CultureInfo.InvariantCulture));
            sb.AppendFormat("<input type='hidden' name='VS' value='{0}' />", this.VS.ToString(CultureInfo.InvariantCulture));
            sb.AppendFormat("<input type='hidden' name='RURL' value='{0}' />", this.RURL);
            sb.AppendFormat("<input type='hidden' name='IPC' value='{0}' />", this.IPC);
            sb.AppendFormat("<input type='hidden' name='NAME' value='{0}' />", this.NAME);
            sb.Append("<input type='hidden' name='LANG' value='sk' />");
            sb.Append("<input type='hidden' name='AREDIR' value='1' />");
            sb.AppendFormat("<input type='hidden' name='TIMESTAMP' value='{0}' />", this.TIMESTAMP);
            sb.AppendFormat("<input type='hidden' name='HMAC' value='{0}' />", this.HMAC);

            return sb.ToString();
        }

        /// <summary>
        /// SelfTest to ensure that our hash/encryption algorithms are correct.
        /// Throws an Exception when selftest did not pass.
        /// </summary>
        /// <returns></returns>
        public static TbCardPay SelfTest()
        {
            TbCardPay request = TbCardPay.CreateRequest(1234.5, Currency.EUR, 1111, "https://moja.tatrabanka.sk/cgi-bin/e-commerce/start/example.jsp", "1.2.3.4", "Jan Pokusny");
            request.MID = 9999;
            request.HexEncryptKey = "31323334353637383930313233343536373839303132333435363738393031323132333435363738393031323334353637383930313233343536373839303132";
            request.FormActionUrl = "https://moja.tatrabanka.sk/cgi-bin/e-commerce/start/cardpay";
            request.TIMESTAMP = "01092014125505";
            request.isSelfTestRunning = true;

            if (request.HMAC != "574b763f4afd4167b10143d71dc2054615c3fa76877dc08a7cc9592a741b3eb5")
            {
                throw new ApplicationException("TbCardPay self request test not passed");
            }

            if (TbCardPay.CheckBankResponse("1234.50", "978", 1111, "OK", "123456", "1", "01092014125505", "8df96c2603831046d0e3502cab1ddb7d9b629d7f09a44aee7abbec0be3f2d971", applicationHexEncryptKey: request.HexEncryptKey) == false)
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
                hex.AppendFormat("{0:x2}", b);
            }

            return hex.ToString();
        }

        /// <summary>
        /// AWARE: This will always returns 64 bytes array (padded with zero values) for AES-256 key 
        /// </summary>
        /// <param name="hex"></param>
        /// <returns></returns>
        public static byte[] HexStringTo64ByteArray(String hex)
        {
            int numOfChars = hex.Length;
            byte[] bytes = new byte[64];
            for (int i = 0; i < numOfChars; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }

            return bytes;
        }

        public static string CardPaySign(string toSign, string hexEncryptKey)
        {
            byte[] toHash = Encoding.ASCII.GetBytes(toSign);
            byte[] key = HexStringTo64ByteArray(hexEncryptKey);
            byte[] hash;
            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                hash = hmac.ComputeHash(toHash);
            }

            return ByteArrayToHexString(hash);
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