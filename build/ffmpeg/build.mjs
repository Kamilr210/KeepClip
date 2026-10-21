// SPDX-License-Identifier: GPL-3.0-or-later
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';

const recipe = path.dirname(fileURLToPath(import.meta.url));
const lock = JSON.parse(fs.readFileSync(path.join(recipe,'sources.lock.json'),'utf8').replace(/^\uFEFF/,''));
const args=process.argv.slice(2);
const output=path.resolve(args[args.indexOf('--output')+1] || '');
if (!args.includes('--output') || output===recipe) throw Error('Use --output with a separate artifact directory.');
const sourceDir=args.includes('--sources')?path.resolve(args[args.indexOf('--sources')+1]):null;
const work=fs.mkdtempSync(path.join(os.tmpdir(),'keepclip-ffmpeg-build-'));
const image='keepclip-ffmpeg:'+crypto.randomBytes(8).toString('hex');
let container;
const sha=file=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
function run(command,parameters,options={}) {
    const result=spawnSync(command,parameters,{stdio:'inherit',...options});
    if (result.error || result.status!==0) throw result.error||Error(`${command} failed (${result.status}).`);
    return result;
}
async function download(url,destination) {
    const response=await fetch(url);
    if (!response.ok) throw Error(`Download failed (${response.status}): ${url}`);
    const data=Buffer.from(await response.arrayBuffer());
    fs.writeFileSync(destination,data);
}
try {
    if (fs.existsSync(output) && fs.readdirSync(output).length) throw Error('Artifact output must be empty; existing files are preserved.');
    fs.mkdirSync(output,{recursive:true});
    fs.mkdirSync(path.join(work,'sources'));
    for (const file of ['Dockerfile','build.sh','sources.lock.json','build.mjs','README.md']) {
        fs.copyFileSync(path.join(recipe,file),path.join(work,file));
    }
    for (const entry of lock.sources) {
        const destination=path.join(work,'sources',entry.file);
        const saved=sourceDir&&path.join(sourceDir,entry.file);
        if (saved&&fs.existsSync(saved)) fs.copyFileSync(saved,destination);
        else {
            console.log(`Downloading ${entry.component} ${entry.version}`);
            await download(entry.url,destination);
        }
        if (sha(destination)!==entry.sha256) throw Error(`Source checksum mismatch: ${entry.file}`);
    }
    run('docker',['build','--progress=plain','-t',image,work]);
    container=spawnSync('docker',['create',image],{encoding:'utf8'});
    if (container.status!==0) throw Error(container.stderr);
    container=container.stdout.trim();
    run('docker',['cp',`${container}:/out/.`,output]);
    const notices=path.join(output,'notices');
    const build=path.join(notices,'build');
    for (const file of ['Dockerfile','build.sh','sources.lock.json','build.mjs','README.md']) {
        fs.copyFileSync(path.join(recipe,file),path.join(build,file));
    }
    fs.copyFileSync(path.join(recipe,'README.md'),path.join(notices,'README.md'));
    const files=[];
    function walk(directory) {
        for(const entry of fs.readdirSync(directory,{withFileTypes:true})) {
            const file=path.join(directory,entry.name);
            if(entry.isDirectory()) walk(file);
            else files.push({file:path.relative(notices,file).replaceAll('\\','/'),sha256:sha(file)});
        }
    }
    walk(notices);
    const binaries=['ffmpeg.exe','ffprobe.exe'].map(file=>({file,sha256:sha(path.join(output,'bin',file)),version:lock.buildId}));
    fs.writeFileSync(path.join(notices,'provenance.json'),JSON.stringify({schemaVersion:2,buildId:lock.buildId,correspondingSourceStatus:'built-from-bundled-sources',distributionAllowed:true,sourceLicense:'GPL-3.0-or-later',binaries,files,sources:lock.sources,baseImage:lock.baseImage},null,2)+'\n');
    console.log(`Built binaries and corresponding sources: ${output}`);
} finally {
    if (typeof container==='string') spawnSync('docker',['rm',container],{stdio:'ignore'});
    spawnSync('docker',['image','rm',image],{stdio:'ignore'});
    fs.rmSync(work,{recursive:true,force:true});
}
