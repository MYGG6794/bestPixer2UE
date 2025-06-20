#!/usr/bin/env node

/**
 * bestPixer2UE 架构验证脚本
 * 测试PeerStreamEnterprise集成和配置正确性
 */

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

console.log('🔍 bestPixer2UE 架构验证');
console.log('========================');

// 检查Node.js版本
console.log(`✅ Node.js版本: ${process.version}`);

// 检查当前目录
const currentDir = process.cwd();
console.log(`📁 当前目录: ${currentDir}`);

// 检查必要文件
const requiredFiles = ['signal.js', 'execue.js', 'signal.json'];
let allFilesExist = true;

requiredFiles.forEach(file => {
    if (fs.existsSync(file)) {
        console.log(`✅ ${file} 存在`);
    } else {
        console.log(`❌ ${file} 缺失`);
        allFilesExist = false;
    }
});

if (!allFilesExist) {
    console.log('\n❌ 缺少必要文件，请确保在PeerStreamEnterprise-main目录中运行');
    process.exit(1);
}

// 读取并验证配置
try {
    const config = JSON.parse(fs.readFileSync('signal.json', 'utf8'));
    console.log(`✅ 配置文件读取成功`);
    console.log(`📡 信令端口: ${config.PORT}`);
    console.log(`🖥️  分辨率: ${config.globlesetting.ResX}x${config.globlesetting.ResY}`);
    console.log(`🎮 FPS: ${config.globlesetting.WebRTCFps}`);
    
    if (config.ueprogram && config.ueprogram.length > 0) {
        console.log(`🎯 配置的UE程序数量: ${config.ueprogram.length}`);
        config.ueprogram.forEach((prog, index) => {
            console.log(`   ${index + 1}. ${prog.name} (${prog.urlprefix})`);
            if (fs.existsSync(prog.path)) {
                console.log(`      ✅ 路径有效: ${prog.path}`);
            } else {
                console.log(`      ⚠️  路径不存在: ${prog.path}`);
            }
        });
    }
} catch (error) {
    console.log(`❌ 配置文件读取失败: ${error.message}`);
    process.exit(1);
}

// 检查端口是否可用
const net = require('net');
function checkPort(port) {
    return new Promise((resolve) => {
        const server = net.createServer();
        server.listen(port, () => {
            server.once('close', () => {
                resolve(true);
            });
            server.close();
        });
        server.on('error', () => {
            resolve(false);
        });
    });
}

async function main() {
    const config = JSON.parse(fs.readFileSync('signal.json', 'utf8'));
    const portAvailable = await checkPort(config.PORT);
    
    if (portAvailable) {
        console.log(`✅ 端口 ${config.PORT} 可用`);
    } else {
        console.log(`⚠️  端口 ${config.PORT} 被占用`);
    }
    
    console.log('\n🚀 测试启动signal.js (5秒后自动停止)');
    
    // 测试启动signal.js
    const signalProcess = spawn('node', ['signal.js'], {
        stdio: ['pipe', 'pipe', 'pipe']
    });
    
    let signalStarted = false;
    
    signalProcess.stdout.on('data', (data) => {
        const output = data.toString();
        console.log(`[Signal] ${output.trim()}`);
        if (output.includes('started') || output.includes('listening') || output.includes(config.PORT)) {
            signalStarted = true;
        }
    });
    
    signalProcess.stderr.on('data', (data) => {
        console.log(`[Signal Error] ${data.toString().trim()}`);
    });
    
    // 5秒后停止测试
    setTimeout(() => {
        signalProcess.kill();
        if (signalStarted) {
            console.log('\n✅ signal.js 启动测试成功');
        } else {
            console.log('\n⚠️  signal.js 启动可能有问题');
        }
        
        console.log('\n📋 验证总结:');
        console.log('- Node.js环境: ✅');
        console.log('- 必要文件: ✅');
        console.log('- 配置文件: ✅');
        console.log(`- 端口${config.PORT}: ${portAvailable ? '✅' : '⚠️'}`);
        console.log(`- signal.js启动: ${signalStarted ? '✅' : '⚠️'}`);
        
        console.log('\n🎯 下一步:');
        console.log('1. 在bestPixer2UE中点击"启动PeerStreamEnterprise服务"');
        console.log('2. 配置并启动UE项目');
        console.log(`3. 浏览器访问: http://127.0.0.1:${config.PORT}`);
    }, 5000);
}

main().catch(console.error);
