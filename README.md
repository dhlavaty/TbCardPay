TbCardPay - minimalist C# Tatrabanka CardPay implementation (HMAC SHA256)
==================================================
version 0.2.0 (2015-08-03)  
(c) 2014 Dušan Hlavatý (dhlavaty@gmail.com)  
freely distributable under The MIT License (MIT)  
https://github.com/dhlavaty/TbCardPay


Purpose (EN):
-------------

Minimalist (single file, single class, no external dependencies) implementation of Tatrabanka CardPay (HMAC SHA256 version) payment gateway written in C#. No dependencies. Contains self-test (poor-mans unit test) to ensure correct implementation.

Popis (SK):
-----------

Minimalistická (iba jeden súbor, jedna trieda, žiadne externé závislosti ani knižnice) implementácia platobného systému Tatrabanka CardPay (verzia HMAC SHA256). Obsahuje self-test, ktorý zaručuje správnu implementáciu hash algoritmov.

Setup / Nastavenie:
-------------------

    (web.config - replace values with ones received from Tatrabanka)
    
    <add key="CardPay:Mid" value="9999" />
    <add key="CardPay:HexEncryptKey" value="1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D" />
    <add key="CardPay:FormActionUrl" value="https://moja.tatrabanka.sk/cgi-bin/e-commerce/start/cardpay" />


Usage / Použitie:
-----------------

### Payment request / Žiadosť o platbu

    // initialize your.cs file
    TbCardPay cardPay = TbCardPay.CreateRequest(132.15,
                                                TbCardPay.Currency.EUR,
                                                1234567890, 
                                                "http://my.eshop.com/CardPayReturnAddress", 
                                                "123.123.123.123", 
                                                "Jan Pal");
                                                
                                                
    <!-- your view or html file - assume that 'cardPay' is inicialized TbCardPay instance, see above -->
    <form action="@cardPay.FormActionUrl" method="post">
        @Html.Raw(cardPay.HtmlHiddenFields())
        
        <button type="submit">Pay</button>
    </form>

### Payment response / Odpoveď z banky

    // MVC example
    public virtual ActionResult CardPayResponse(string amt, string curr, int vs, string res, string ac, string tid, string timestamp, string hmac)
    {
        if (false == TbCardPay.CheckBankResponse(amt, curr, vs, res, ac, tid, timestamp, hmac))
        {
            // SIGN from bank is NOT valid - log this error and exit
            return new HttpNotFoundResult();
        }

        if (res == "OK")
        {
            // ... payment was OK, you can pair your payments using "vs" parameter
        } else {
            // ... payment has failed (maybe credit card was expired)
        }
    }

### Self-test (EN)

It is not necessary to run self-test, it is run automatically. But you can:

    TbCardPay.SelfTest();

### Self-test (SK)

Self-test nie je potrebné spúšťať manuláne, spúšťa sa automaticky. No ak chcete, môžete takto:

    TbCardPay.SelfTest();

Changelog:
----------

* 2015-08-03 ver 0.2.0
   - changed to HMAC SHA256 version

* 2014-04-10 ver 0.1.0
   - initial release
