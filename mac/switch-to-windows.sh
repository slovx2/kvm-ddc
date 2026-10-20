#!/bin/bash

# Required parameters:
# @raycast.schemaVersion 1
# @raycast.title 显示器切到 Windows
# @raycast.mode silent

# Optional parameters:
# @raycast.icon 🖥️
# @raycast.packageName Display
# @raycast.description 通过 DDC/CI 把 U2725QE 切到 DP 口（Windows），显示器 KVM 会把键鼠一起带过去

# U2725QE 输入源：DP(Windows)=15，雷电(Mac)=25，2026-10-19 实测
/opt/homebrew/bin/m1ddc set input 15
