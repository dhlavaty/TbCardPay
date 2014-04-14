TbCardPay - minimalist C# Tatrabanka CardPay implementation
==================================================
version 0.1.0 (2014-04-10)  
(c) 2014 Dušan Hlavatý (dhlavaty@gmail.com)  
freely distributable under The MIT License (MIT)  
https://github.com/dhlavaty/TbCardPay


Purpose (EN):
-------------

Minimalist (single file, single class, no external dependencies) implementation of Tatrabanka CardPay (AES-256 version) payment gateway written in C#. No dependencies Contains self-test (poor-mans unit test) to ensure implementation is correct.

Popis (SK):
-----------

Minimalistická (iba jeden súbor, jedna trieda, žiadne externé závislosti ani knižnice) implementácia platobného systému Tatrabanka CardPay (verzia AES-256). Obsahuje self-test, ktorý zaručuje správnu implementáciu šifrovacích algoritmov.

Setup / Nastavenie:
-------------------

    (web.config - replace values with ones received from Tatrabanka)
    
    <add key="CardPay:Mid" value="9999" />
    <add key="CardPay:HexEncryptKey" value="1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D1A2B3C4D" />
    <add key="CardPay:FormActionUrl" value="http://localhost/" />


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
    <form action="@cardPay.FormActionUrl" enctype="application/x-www-form-urlencoded" method="post">
        @Html.Raw(cardPay.HtmlHiddenFields())
        
        <button type="submit">Pay</button>
    </form>

### Payment response / Odpoveď z banky

    // MVC example
    public virtual ActionResult CardPayResponse(int vs, string res, string ac = null, string sign = null)
    {
        if (false == TbCardPay.CheckBankResponse(vs, res, ac, sign))
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

It is not necessary to run self-test, it is run automatically. But if you wish:

    TbCardPay.SelfTest();

### Self-test (SK)

Self-test nie je potrebné spúšťať manuláne, spúšťa sa automaticky. No ak chcete, môžete takto:

    TbCardPay.SelfTest();

Changelog:
----------

* 2014-04-10 ver 0.1.0
   - initial release
