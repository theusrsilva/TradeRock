The game's local wireless is **encrypted**. The **prod.keys** file holds the Switch keys needed to
understand that conversation. Without it, TradeRock can't talk to your console.

**How to get it:** dump it from **your own** Switch with **Lockpick_RCM**, on a hackable Switch 1
(vulnerable models or with a modchip). The **Switch 2 can't** have its keys dumped.

> ⚠ **Important:** the keys are Nintendo's protected material. Use **only** keys dumped from your
> own console. **Don't download** them and **don't share** the file: part of the keys is unique to
> your console. TradeRock never downloads keys or points to sources.

**What TradeRock does with the file:** stores it in a folder only you can read (mode 600), uses it
only to talk to the Switch, and **never** shows the values, logs them, sends them over the network,
or puts them in backups, exports or reports.

<details><summary>Learn more</summary>

Here we only check the **format**: that the file is readable and has the keys the LDN library uses
(`aes_kek_generation_source`, `aes_key_generation_source` and `master_key_00` or `master_key_12`).
That **doesn't prove** the values are right. The proof comes in the **Switch test** (last item of the list): if
TradeRock can decrypt your game's room, the keys are right.

</details>

Don't have the file yet? You can **skip** and explore the app: create Pokémon, organize the vault
and try trading in **demo mode**. Only real trading stays locked.
