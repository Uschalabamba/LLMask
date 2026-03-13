class FrequencyOrderingCase
{
    // Rare is declared first but called only once.
    // Common is declared second but called five times.
    // Common should get SomeMethod1, Rare should get SomeMethod2.

    void Rare() { }

    void Common() { }

    void Driver()
    {
        Common();
        Common();
        Common();
        Common();
        Common();
        Rare();
    }
}
