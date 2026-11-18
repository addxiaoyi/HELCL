export function nearbyMap(bot) {
    const center = bot.entity.position.floored();
    const radius = 8;
    const tiles = [];
    for (let z = -radius; z <= radius; z++)
        for (let x = -radius; x <= radius; x++) {
            let tile = { x: center.x + x, z: center.z + z, y: center.y - 1, name: '', kind: 'unknown' };
            for (let y = 5; y >= -6; y--) {
                const block = bot.blockAt(center.offset(x, y, z));
                if (!block)
                    break;
                if (['air', 'cave_air', 'void_air'].includes(block.name))
                    continue;
                const kind = block.name.includes('water') ? 'water' : block.name.includes('lava') ? 'lava'
                    : /leaves|_log/.test(block.name) ? 'tree' : /grass|moss/.test(block.name) ? 'grass'
                        : /sand|gravel/.test(block.name) ? 'sand' : /snow|ice/.test(block.name) ? 'snow' : 'solid';
                tile = { x: block.position.x, y: block.position.y, z: block.position.z, name: block.name, kind };
                break;
            }
            tiles.push(tile);
        }
    return { center: { x: center.x, y: center.y, z: center.z }, radius, tiles, note: '仅显示已加载区块、当前位置上下有限高度的地形，未知区域不推测。' };
}
