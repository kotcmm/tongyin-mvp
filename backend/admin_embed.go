package main

import _ "embed"

// 管理后台 HTML、CSS、JS 全部嵌入 Go 二进制文件。
//
//go:embed webadmin/index.html
var embeddedAdminHTML []byte

//go:embed webadmin/app.css
var embeddedAdminCSS []byte

//go:embed webadmin/core.js
var embeddedAdminCore []byte

//go:embed webadmin/views.js
var embeddedAdminViews []byte

//go:embed webadmin/actions.js
var embeddedAdminActions []byte

//go:embed webadmin/ai-admin.js
var embeddedAdminAI []byte
