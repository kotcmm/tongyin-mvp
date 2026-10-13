package main

import _ "embed"

// The entire operating console is bundled into the Go binary.
// Admin uses vanilla HTML/CSS/JS: no Node server or second deployment.
//go:embed webadmin/index.html
var embeddedAdminHTML []byte
