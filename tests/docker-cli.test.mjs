import assert from 'node:assert/strict';
import {test} from 'node:test';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {resolveDocker} from '../build/ffmpeg/docker-cli.mjs';

test('Windows selects Docker Desktop independently of PATH',()=>{
    const visited=[];
    const expected='C:\\Program Files\\Docker\\Docker\\resources\\bin\\docker.exe';
    const result=resolveDocker({platform:'win32',env:{ProgramFiles:'C:\\Program Files',PATH:'.;C:\\untrusted'},isFile:file=>{visited.push(file);return true;}});
    assert.equal(result,expected);
    assert.deepEqual(visited,[expected]);
});

test('Windows uses the native Program Files directory for a 32-bit Node process',()=>{
    const result=resolveDocker({platform:'win32',env:{ProgramW6432:'C:\\Program Files',ProgramFiles:'C:\\Program Files (x86)'},isFile:()=>true});
    assert.equal(result,'C:\\Program Files\\Docker\\Docker\\resources\\bin\\docker.exe');
});

test('Windows has a fixed fallback when Program Files variables are unavailable',()=>{
    assert.equal(resolveDocker({platform:'win32',env:{},isFile:()=>true}),'C:\\Program Files\\Docker\\Docker\\resources\\bin\\docker.exe');
});

for (const candidate of ['/usr/bin/docker','/usr/local/bin/docker','/opt/homebrew/bin/docker']) {
    test(`Unix finds a trusted installation at ${candidate}`,()=>{
        assert.equal(resolveDocker({platform:'linux',env:{PATH:'.:/untrusted'},isFile:file=>file===candidate}),candidate);
    });
}

for (const [platform,configured] of [['win32','docker.exe'],['win32','C:docker.exe'],['win32','\\docker.exe'],['win32',''],['linux','./docker'],['linux','']]) {
    test(`${platform} rejects the relative or empty override ${JSON.stringify(configured)}`,()=>{
        assert.throws(()=>resolveDocker({platform,env:{KEEPCLIP_DOCKER:configured},isFile:()=>assert.fail('Must reject before accessing files')}),/absolute executable path/);
    });
}

test('An explicit trusted executable can be outside the standard directories',()=>{
    const executable='D:\\Custom Tools\\docker.exe';
    assert.equal(resolveDocker({platform:'win32',env:{KEEPCLIP_DOCKER:executable},isFile:file=>file===executable}),executable);
});

test('A missing explicit override does not silently fall back to another executable',()=>{
    const visited=[];
    assert.throws(()=>resolveDocker({platform:'linux',env:{KEEPCLIP_DOCKER:'/missing/docker'},isFile:file=>{visited.push(file);return false;}}),/Docker CLI not found/);
    assert.deepEqual(visited,['/missing/docker']);
});

test('Missing candidates are skipped, while permission errors are reported',()=>{
    for (const code of ['ENOENT','ENOTDIR']) {
        const result=resolveDocker({platform:'linux',env:{},isFile:file=>{
            if (file==='/usr/bin/docker') throw Object.assign(new Error('fixture'),{code});
            return true;
        }});
        assert.equal(result,'/usr/local/bin/docker');
    }
    const error=Object.assign(new Error('Access denied'),{code:'EACCES'});
    assert.throws(()=>resolveDocker({platform:'linux',env:{},isFile:()=>{throw error;}}),value=>value===error);
});

test('Untrusted PATH entries are never probed, even if system installations are missing',()=>{
    const visited=[];
    assert.throws(()=>resolveDocker({platform:'linux',env:{PATH:'.:/untrusted'},isFile:file=>{visited.push(file);return false;}}),/Docker CLI not found/);
    assert.deepEqual(visited,['/usr/bin/docker','/usr/local/bin/docker','/opt/homebrew/bin/docker']);
});

test('A relative Program Files value is never used to locate an executable',()=>{
    assert.throws(()=>resolveDocker({platform:'win32',env:{ProgramFiles:'untrusted'},isFile:()=>assert.fail('Must not probe relative paths')}),/Docker CLI not found/);
});

test('Filesystem validation accepts files and rejects directories and missing paths',()=>{
    const directory=fs.mkdtempSync(path.join(os.tmpdir(),'keepclip-docker-test-'));
    try {
        const executable=path.join(directory,'docker.exe');
        fs.writeFileSync(executable,'test fixture, never executed');
        assert.equal(resolveDocker({env:{KEEPCLIP_DOCKER:executable}}),executable);
        assert.throws(()=>resolveDocker({env:{KEEPCLIP_DOCKER:directory}}),/Docker CLI not found/);
        assert.throws(()=>resolveDocker({env:{KEEPCLIP_DOCKER:path.join(directory,'missing')}}),/Docker CLI not found/);
    } finally { fs.rmSync(directory,{recursive:true,force:true}); }
});