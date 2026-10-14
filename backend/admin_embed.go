package main

import _ "embed"

// 管理后台的页面、脚本和样式都打进 Go 程序，仅需部署一个进程。
//go:embed webadmin/index.html
var embeddedAdminHTML []byte

//go:embed webadmin/app.js
var embeddedAdminJS []byte

//go:embed webadmin/app.css
var embeddedAdminCSS []byte
