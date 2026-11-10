import pathfinding from 'mineflayer-pathfinder';
const { goals } = pathfinding;
const pause = (milliseconds) => new Promise(resolve => setTimeout(resolve, milliseconds));
export const logNames = new Set(['oak_log', 'spruce_log', 'birch_log', 'jungle_log', 'acacia_log', 'dark_oak_log', 'mangrove_log', 'cherry_log', 'pale_oak_log']);
export const inventory = (bot) => bot.inventory.items().map(item => ({ name: item.name, count: item.count, slot: item.slot }));
const countItem = (bot, name) => bot.inventory.items().filter(item => item.name === name).reduce((count, item) => count + item.count, 0);
export async function followPlayer(bot, player, seconds, distance, check) {
    const target = bot.players[player]?.entity;
    if (!target || target === bot.entity || target.position.distanceTo(bot.entity.position) > 32)
        throw new Error('指定玩家不在附近，请先观察玩家名。');
    bot.pathfinder.setGoal(new goals.GoalFollow(target, distance), true);
    const until = Date.now() + seconds * 1000;
    try {
        while (Date.now() < until) {
            check();
            if (bot.entities[target.id] !== target || target.position.distanceTo(bot.entity.position) > 64)
                throw new Error('跟随目标已离开可见范围，已停止跟随。');
            await pause(100);
        }
        check();
        return { player, seconds, distance: Math.round(target.position.distanceTo(bot.entity.position) * 10) / 10 };
    }
    finally {
        bot.pathfinder.setGoal(null);
        bot.clearControlStates();
    }
}
export async function pickupItems(bot, radius, limit, check, name) {
    const nearby = Object.values(bot.entities).filter(entity => {
        const item = entity.getDroppedItem?.();
        return item && (!name || item.name === name) && entity.position.distanceTo(bot.entity.position) <= radius;
    }).sort((a, b) => a.position.distanceTo(bot.entity.position) - b.position.distanceTo(bot.entity.position)).slice(0, limit);
    let collected = 0;
    for (const target of nearby) {
        check();
        const item = target.getDroppedItem();
        if (!item || !bot.entities[target.id])
            continue;
        const before = countItem(bot, item.name);
        // Drops briefly hover after spawning; path to their block, then wait for the inventory update.
        const ground = target.position.floored();
        await bot.pathfinder.goto(new goals.GoalBlock(ground.x, ground.y, ground.z));
        check();
        for (let attempt = 0; attempt < 15 && countItem(bot, item.name) === before; attempt++) {
            check();
            await pause(100);
        }
        collected += Math.max(0, countItem(bot, item.name) - before);
    }
    return { candidates: nearby.length, collected, inventory: inventory(bot) };
}
export function hasLeaves(bot, origin) {
    for (let x = -3; x <= 3; x++)
        for (let y = 1; y <= 7; y++)
            for (let z = -3; z <= 3; z++) {
                const block = bot.blockAt(origin.offset(x, y, z));
                if (block?.name.endsWith('_leaves'))
                    return true;
            }
    return false;
}
export async function collectWood(bot, name, count, radius, check) {
    if (!logNames.has(name))
        throw new Error('自动采木仅支持天然原木，不处理木板或去皮木。');
    const type = bot.registry.blocksByName[name];
    if (!type)
        throw new Error('当前版本没有此原木。');
    const trees = bot.findBlocks({ matching: type.id, maxDistance: radius, count: 64 }).filter(position => hasLeaves(bot, position));
    if (!trees.length)
        throw new Error('附近没有带树叶的目标原木，未拆除木结构。');
    const before = countItem(bot, name);
    let harvested = 0;
    const axe = bot.inventory.items().find(item => /^(diamond|netherite|iron|stone|golden|wooden)_axe$/.test(item.name));
    if (axe) {
        await bot.equip(axe, 'hand');
        check();
    }
    for (const position of trees.sort((a, b) => a.y - b.y || a.distanceTo(bot.entity.position) - b.distanceTo(bot.entity.position))) {
        check();
        if (harvested >= count)
            break;
        let block = bot.blockAt(position);
        if (block?.name !== name)
            continue;
        if (position.distanceTo(bot.entity.position) > 3.5)
            await bot.pathfinder.goto(new goals.GoalNear(position.x, bot.entity.position.y, position.z, 2));
        check();
        block = bot.blockAt(position);
        const feet = bot.entity.position.floored();
        if (!block || block.name !== name || position.distanceTo(bot.entity.position) > 5 ||
            (position.x === feet.x && position.z === feet.z && position.y < feet.y) || !bot.canDigBlock(block))
            continue;
        await bot.dig(block);
        check();
        if (bot.blockAt(position)?.name === name)
            throw new Error('服务器没有确认原木被采集。');
        harvested++;
    }
    if (!harvested)
        throw new Error('找到了树，但没有可到达的原木。');
    await pause(1000);
    check();
    await pickupItems(bot, 8, 8, check, name);
    return { requested: count, harvested, collected: Math.max(0, countItem(bot, name) - before), inventory: inventory(bot) };
}
