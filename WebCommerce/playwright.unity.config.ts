import { defineConfig } from '@playwright/test';
export default defineConfig({outputDir:'./test-results/unity',testDir:'./e2e-unity',timeout:180000,workers:1,retries:0,reporter:[['list'],['json',{outputFile:'../.artifacts/acceptance/unity-results.json'}]],use:{baseURL:'http://127.0.0.1:5186',viewport:{width:1280,height:720},trace:'retain-on-failure',screenshot:'only-on-failure'}});
