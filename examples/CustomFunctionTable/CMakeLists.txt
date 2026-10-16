cmake_minimum_required(VERSION 3.24)
project(BGCSCustomFunctionTable LANGUAGES C)
add_library(custom_table SHARED native.c)
set_target_properties(custom_table PROPERTIES C_STANDARD 17 C_STANDARD_REQUIRED YES C_VISIBILITY_PRESET hidden)
