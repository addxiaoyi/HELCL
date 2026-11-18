export async function craftItem(bot, name, times, check) {
    const item = bot.registry.itemsByName[name];
    if (!item)
        throw new Error('游戏版本中没有这个物品。');
    const tableId = bot.registry.blocksByName.crafting_table?.id;
    const table = tableId === undefined ? null : bot.findBlock({ matching: tableId, maxDistance: 3 });
    const recipe = bot.recipesFor(item.id, null, 1, table)[0];
    if (!recipe)
        throw new Error('材料不足或附近没有所需工作台。');
    const count = () => bot.inventory.items().filter(entry => entry.name === name).reduce((total, entry) => total + entry.count, 0);
    const before = count();
    const originalClick = bot.clickWindow;
    const sync = bot._syncWindow;
    // 1.17+ click promises can resolve before the server accepts the inventory prediction.
    bot.clickWindow = async (...args) => {
        check();
        await originalClick.apply(bot, args);
        if (sync)
            await sync(bot.currentWindow ?? bot.inventory);
        check();
    };
    try {
        await bot.craft(recipe, times, table ?? undefined);
        if (sync)
            await sync(bot.inventory);
        check();
        if (count() - before < recipe.result.count * times)
            throw new Error('服务器没有确认全部合成物进入背包，请检查背包后再操作。');
        return { item: name, crafted: count() - before };
    }
    finally {
        bot.clickWindow = originalClick;
    }
}
