// SPDX-License-Identifier: GPL-3.0-or-later
import fs from 'node:fs';
import path from 'node:path';

export function resolveDocker({platform=process.platform,env=process.env,isFile=file=>fs.statSync(file).isFile()}={}) {
    const paths=platform==='win32'?path.win32:path.posix;
    const absolute=file=>paths.isAbsolute(file)&&(platform!=='win32'||paths.parse(file).root.length>1);
    const configured=env.KEEPCLIP_DOCKER;
    if (configured!==undefined&&!absolute(configured)) throw Error('KEEPCLIP_DOCKER must be an absolute executable path.');
    const candidates=configured!==undefined?[configured]:platform==='win32'
        ?[paths.join(env.ProgramW6432||env.ProgramFiles||'C:\\Program Files','Docker','Docker','resources','bin','docker.exe')]
        :['/usr/bin/docker','/usr/local/bin/docker','/opt/homebrew/bin/docker'];
    for (const candidate of candidates) {
        if (!absolute(candidate)) continue;
        try { if (isFile(candidate)) return candidate; }
        catch (error) { if (error.code!=='ENOENT'&&error.code!=='ENOTDIR') throw error; }
    }
    throw Error('Docker CLI not found in a system installation directory. Set KEEPCLIP_DOCKER to its absolute executable path.');
}