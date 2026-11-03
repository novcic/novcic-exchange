// SPDX-License-Identifier: MIT
pragma solidity ^0.8.24;
// Minimal fixed-supply ERC20 for the configured local Ethereum network.
contract Token {
    string public name;
    string public symbol;
    uint8 public immutable decimals;
    uint256 public totalSupply;
    mapping(address => uint256) public balanceOf;
    mapping(address => mapping(address => uint256)) public allowance;
    event Transfer(address indexed from, address indexed to, uint256 value);
    event Approval(address indexed owner, address indexed spender, uint256 value);
    constructor(string memory n, string memory s, uint8 d, uint256 supply) {
        name=n; symbol=s; decimals=d; totalSupply=supply; balanceOf[msg.sender]=supply;
        emit Transfer(address(0),msg.sender,supply);
    }
    function transfer(address to,uint256 value) external returns(bool) { _move(msg.sender,to,value); return true; }
    function approve(address spender,uint256 value) external returns(bool) { allowance[msg.sender][spender]=value; emit Approval(msg.sender,spender,value); return true; }
    function transferFrom(address from,address to,uint256 value) external returns(bool) {
        uint256 a=allowance[from][msg.sender]; require(a>=value,"allowance");
        if(a!=type(uint256).max) allowance[from][msg.sender]=a-value;
        _move(from,to,value); return true;
    }
    function _move(address from,address to,uint256 value) internal {
        require(to!=address(0),"zero address"); require(balanceOf[from]>=value,"balance");
        balanceOf[from]-=value; balanceOf[to]+=value; emit Transfer(from,to,value);
    }
}
