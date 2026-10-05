#pragma once

class BridgeBaseA {
public:
    virtual ~BridgeBaseA() = default;
    int first = 11;
};

class BridgeBaseB {
public:
    virtual ~BridgeBaseB() = default;
    double second = 13;
};

class BridgeCounter : public BridgeBaseA, public BridgeBaseB {
    inline static int s_liveCount = 0;
    int m_value;

public:
    explicit BridgeCounter(int value) : m_value(value) {
        ++s_liveCount;
    }

    ~BridgeCounter() override {
        --s_liveCount;
    }

    int Add(int amount) {
        m_value += amount;
        return m_value;
    }

    int Read() const {
        return m_value;
    }

    static int LiveCount() {
        return s_liveCount;
    }
};
