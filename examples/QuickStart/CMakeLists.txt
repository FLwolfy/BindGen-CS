cmake_minimum_required(VERSION 3.20)
project(bgcs_quickstart LANGUAGES C)
add_library(quickstart SHARED native.c)
set_property(TARGET quickstart PROPERTY C_STANDARD 17)
